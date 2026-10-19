using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Pathfinding;
using Brix.Pathfinding.Test;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Pathfinding;

// Where a faction's workers can walk to: every place of the route graph connected to where one of them stands, by
// the rules their own route search uses (walls they can climb, doors of their own faction, no forest, no breaking
// through). The game's own reachability map (Knowledge) is coarser and says yes to places no route reaches (logs
// behind a wall the workers then walk to again and again), so this one is flooded over the fine graph itself. A
// flood goes through a few thousand places per frame and starts again when the world changed or every so often
// (the workers move); the last finished one is the map in use. Host only.
internal static class ReachMap
{
	// Graph places visited per frame by a running flood.
	private const int NodesPerFrame = 4000;

	// A new flood at the latest this long after the last one, even with no change (workers move to other places).
	private const float RefreshSeconds = 20f;

	// A new flood at the earliest this long after the last one, after a change.
	private const float ChangeDelaySeconds = 1f;

	private sealed class Flood
	{
		public Faction Faction;

		public HashSet<XYZ> Reached = new HashSet<XYZ>();

		public Queue<PathNode> Open = new Queue<PathNode>();

		public HashSet<PathNode> Seen = new HashSet<PathNode>();

		public SearchPathRequest.BranchEvaluator Evaluator;
	}

	// The finished map of each faction.
	private static readonly Dictionary<Faction, HashSet<XYZ>> Maps = new Dictionary<Faction, HashSet<XYZ>>();

	private static Flood _running;

	private static readonly Queue<Faction> Pending = new Queue<Faction>();

	private static float _lastStart = -1000f;

	private static bool _changed;

	// Counts up each time a flood finishes, so users know a fresh map is there.
	internal static int Generation { get; private set; }

	internal static void Clear()
	{
		Maps.Clear();
		Pending.Clear();
		_running = null;
		_lastStart = -1000f;
		_changed = false;
		Generation = 0;
	}

	internal static void MarkChanged()
	{
		_changed = true;
	}

	internal static bool Has(Faction faction)
	{
		return faction != null && Maps.ContainsKey(faction);
	}

	// Whether a worker of the faction can stand at the place (a node of the fine graph).
	internal static bool Contains(Faction faction, XYZ place)
	{
		return faction != null && Maps.TryGetValue(faction, out HashSet<XYZ> map) && map.Contains(place);
	}

	// Whether a worker can stand beside the place (its own column or up to beside columns out), level with it, up to
	// down blocks lower or up to up blocks higher: from there it reaches it.
	internal static bool Near(Faction faction, XYZ place, int beside, int down, int up)
	{
		if (faction == null || !Maps.TryGetValue(faction, out HashSet<XYZ> map))
		{
			return false;
		}
		for (int dx = -beside; dx <= beside; dx++)
		{
			for (int dz = -beside; dz <= beside; dz++)
			{
				for (int dy = -down; dy <= up; dy++)
				{
					if (map.Contains(new XYZ(place.x + dx, place.y + dy, place.z + dz)))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	// Every frame on the host.
	internal static void Update()
	{
		if (!NetworkServer.active)
		{
			return;
		}
		TestPathfinding pathfinding = TestPathfinding.Instance;
		if (pathfinding == null || !pathfinding.Initialized || pathfinding.terrainLod == null)
		{
			return;
		}
		if (_running == null)
		{
			if (Pending.Count == 0)
			{
				float since = Time.time - _lastStart;
				if (since < ChangeDelaySeconds || (!_changed && since < RefreshSeconds))
				{
					return;
				}
				_changed = false;
				_lastStart = Time.time;
				foreach (Faction faction in WorkerFactions())
				{
					Pending.Enqueue(faction);
				}
				if (Pending.Count == 0)
				{
					return;
				}
			}
			_running = Start(Pending.Dequeue(), pathfinding);
			if (_running == null)
			{
				return;
			}
		}
		if (Step(_running, NodesPerFrame))
		{
			Maps[_running.Faction] = _running.Reached;
			_running = null;
			if (Pending.Count == 0)
			{
				Generation++;
			}
		}
	}

	// The factions with workers of a player (not the AI's corruptrons).
	private static List<Faction> WorkerFactions()
	{
		List<Faction> factions = new List<Faction>();
		foreach (Labor labor in Live<Labor>.Active())
		{
			Faction faction = labor.faction;
			if (faction != null && !faction.isAI && !factions.Contains(faction))
			{
				factions.Add(faction);
			}
		}
		return factions;
	}

	private static Flood Start(Faction faction, TestPathfinding pathfinding)
	{
		Flood flood = new Flood { Faction = faction };
		Labor any = null;
		foreach (Labor labor in Live<Labor>.Active())
		{
			if (labor.faction != faction || labor.presence == null)
			{
				continue;
			}
			any = any ?? labor;
			PathNode node = labor.presence.CurrentPathNode;
			if (node != null && !node.IsEmpty && node.Lod == 0 && flood.Seen.Add(node))
			{
				flood.Open.Enqueue(node);
				flood.Reached.Add(node.RepresentativePosition);
			}
		}
		if (any == null || flood.Open.Count == 0)
		{
			return null;
		}
		GameObject self = any.gameObject;
		bool forest = any.Profession != null && any.Profession.CanWalkInForest();
		flood.Evaluator = new SearchPathRequest.BranchEvaluator((XYZ p) => !SearchPathRequest.HasBlockerAt(self, p), canClimb: true, forest, canBreakVoxels: false);
		return flood;
	}

	// Floods up to count places; true when the flood is done.
	private static bool Step(Flood flood, int count)
	{
		for (int i = 0; i < count; i++)
		{
			if (flood.Open.Count == 0)
			{
				return true;
			}
			PathNode node = flood.Open.Dequeue();
			if (!(node is TerrainPathNode terrain) || terrain.siblings == null)
			{
				continue;
			}
			foreach (PathBranch branch in terrain.siblings)
			{
				PathNode next = branch.Node;
				if (next.IsEmpty || next.Lod != 0 || flood.Seen.Contains(next) || !flood.Evaluator.CanCross(branch))
				{
					continue;
				}
				flood.Seen.Add(next);
				flood.Reached.Add(next.RepresentativePosition);
				flood.Open.Enqueue(next);
			}
		}
		return flood.Open.Count == 0;
	}
}
