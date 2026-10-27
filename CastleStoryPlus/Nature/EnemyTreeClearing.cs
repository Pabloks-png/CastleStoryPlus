using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Legacy;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Nature;

// The enemy waves could not get through trees: a place beside a tree is a "forest" step of the route graph, which only
// a unit already standing in a forest may take, and the enemies break blocks but trees are not blocks. Now that trees
// can be planted, a row of them shut the enemies out of a base for good. Now an enemy that breaks its way (corruptron,
// biftron, minitron, magitron) may plan its route through trees, and fells the trees in its way: a tree beside it
// that lies ahead of it (or any tree beside it while it is stuck) is struck every StrikeSeconds for StrikeDamage, as
// a bricktron's axe strikes it (a tree has 180), so it falls and drops its logs like any felled tree. Host only; the
// tree's fall is the game's own and reaches every player.
[Feature(Features.EnemyTreeClearing, Features.EnemyTreeClearingInfo)]
internal class EnemyTreeClearing : MonoBehaviour
{
	private const float CheckSeconds = 0.5f;

	private const float StrikeSeconds = 1.2f;

	private const int StrikeDamage = 60;

	// Moved less than this in a check: stuck.
	private const float StuckDistance = 0.2f;

	private sealed class Watch
	{
		internal Vector3 Last;

		internal float NextStrike;
	}

	private static readonly Dictionary<Labor, Watch> Watched = new Dictionary<Labor, Watch>();

	private static readonly List<Labor> Gone = new List<Labor>();

	private float _nextCheck;

	private static void Enable()
	{
		GameSession.OnLeave(() => Watched.Clear());
		Plugin.Root.AddComponent<EnemyTreeClearing>();
	}

	// An enemy wave unit that breaks its way.
	internal static bool IsClearer(Labor labor)
	{
		Faction faction = (labor != null) ? labor.faction : null;
		return faction != null && faction.isAI && labor.Profession != null && labor.Profession.CanMakeWay() && Treasure.Beasts.KindOf(labor) == null;
	}

	private void Update()
	{
		float now = Time.time;
		if (!NetworkServer.active || now < _nextCheck)
		{
			return;
		}
		_nextCheck = now + CheckSeconds;
		TreeRegistry registry = TreeRegistry.Instance;
		if (registry == null)
		{
			return;
		}
		foreach (Labor labor in Live<Labor>.Active())
		{
			if (labor.IsNullOrReleased() || !IsClearer(labor))
			{
				continue;
			}
			Vector3 position = labor.transform.position;
			if (!Watched.TryGetValue(labor, out Watch watch))
			{
				watch = new Watch { Last = position };
				Watched[labor] = watch;
			}
			bool stuck = (position - watch.Last).sqrMagnitude < StuckDistance * StuckDistance;
			Vector3 moved = position - watch.Last;
			watch.Last = position;
			if (now < watch.NextStrike)
			{
				continue;
			}
			XYZ cell = XYZ.FromVector3(position);
			if (!registry.IsNearATree(cell))
			{
				continue;
			}
			Vector3 heading = stuck ? labor.transform.forward : moved.normalized;
			if (!FindTree(registry, cell, position, heading, stuck, out XYZ tree))
			{
				continue;
			}
			watch.NextStrike = now + StrikeSeconds;
			Strike(labor, tree);
		}
		foreach (KeyValuePair<Labor, Watch> entry in Watched)
		{
			if (entry.Key == null)
			{
				Gone.Add(entry.Key);
			}
		}
		foreach (Labor labor in Gone)
		{
			Watched.Remove(labor);
		}
		Gone.Clear();
	}

	// The tree beside the unit most nearly ahead of it; while it is stuck any tree beside it.
	private static bool FindTree(TreeRegistry registry, XYZ cell, Vector3 position, Vector3 heading, bool stuck, out XYZ tree)
	{
		tree = XYZ.zero;
		float best = stuck ? -2f : 0.3f;
		bool found = false;
		Vector3 flat = new Vector3(heading.x, 0f, heading.z);
		if (flat.sqrMagnitude > 0.0001f)
		{
			flat.Normalize();
		}
		foreach (XYZ offset in TreeRegistry.TreeNeighborhood)
		{
			XYZ at = cell + offset;
			if (!registry.HasTree(at))
			{
				continue;
			}
			Vector3 to = at.ToVector3() - position;
			to.y = 0f;
			float ahead = (to.sqrMagnitude > 0.0001f) ? Vector3.Dot(flat, to.normalized) : 1f;
			if (ahead > best)
			{
				best = ahead;
				tree = at;
				found = true;
			}
		}
		return found;
	}

	private static void Strike(Labor labor, XYZ position)
	{
		DamagedTree tree = DamagedTree.GetAtOrCreate(position);
		if (tree.IsNullOrReleased() || tree.IsDying)
		{
			return;
		}
		Faction faction = labor.affiliation.faction;
		tree.GetOrAddComponent<AffiliationComponent>().faction = faction;
		labor.transform.rotation = Quaternion.LookRotation(Flat(position.ToVector3() - labor.transform.position, labor.transform.forward));
		tree.Strike(position.ToScalaire(), StrikeDamage);
	}

	private static Vector3 Flat(Vector3 v, Vector3 fallback)
	{
		v.y = 0f;
		return (v.sqrMagnitude > 0.0001f) ? v : fallback;
	}
}

// Enemies that break their way may plan routes through trees (which they fell on the way).
[Feature(Features.EnemyTreeClearing, Features.EnemyTreeClearingInfo)]
[HarmonyPatch(typeof(Profession), nameof(Profession.CanWalkInForest))]
internal static class EnemyTreeClearingRoutePatch
{
	private static void Postfix(Profession __instance, ref bool __result)
	{
		if (!__result && EnemyTreeClearing.IsClearer(__instance.GetComponent<Labor>()))
		{
			__result = true;
		}
	}
}
