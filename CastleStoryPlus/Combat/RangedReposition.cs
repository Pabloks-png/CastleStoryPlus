using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.InstructionType;
using Brix.Game.AI.Nodes;
using Brix.Game.AI.Pathfinding;
using Brix.Game.Components;
using Brix.Game.Locomotion;
using Brix.Pathfinding;
using Brix.Pathfinding.Test;
using Brix.UI.Icons;
using CastleStoryPlus.Core;
using CastleStoryPlus.Pathfinding;
using HarmonyLib;
using Motus.Behavior;
using UnityEngine;

namespace CastleStoryPlus.Combat;

// A ranged fighter of a player's team (archer, arbalist, slinger, alchemist, artificer: anyone with a ranged weapon)
// with an enemy in range whose every shot would hit the castle or the rock in between (standing back from a wall's
// edge, behind a merlon) stood still and waited, or ran out to the enemy. Now it first looks for a place near it to
// shoot from, from where the game's own aiming test finds the shot's flight clear, walks there and holds that place
// while it shoots. The places are walked out from its anchor along the route graph's plain steps, up to
// [RangedReposition] MaxDistance blocks (8) out, at most 1 block lower (so it does not climb down off the wall) or 2
// higher, and on ground its team can reach; the nearest walk first. The anchor is where it stood before this first
// moved it, so searching again from each new place does not take it step by step down to the enemy; it is set anew
// when the fighter is no longer where it was sent (moved by the player or a task). The aiming test is the one used
// before every shot, run as if the fighter stood on each place (it is moved there and back within the test; nothing
// sees it move). At most MaxTests places per search; a search that finds nothing is not repeated for RetrySeconds.
// Host only, like the game's own combat decisions.
[Feature(Features.RangedReposition, Features.RangedRepositionInfo)]
internal static class RangedReposition
{
	internal static ConfigEntry<int> MaxDistance;

	private const int MaxDrop = 1;

	private const int MaxRise = 2;

	private const int MaxTests = 60;

	private const float RetrySeconds = 2f;

	private sealed class Place
	{
		internal XYZ Cell;

		internal Vector3 Stand;

		internal int Distance;
	}

	private static readonly Dictionary<Labor, float> NextSearch = new Dictionary<Labor, float>();

	// The place each fighter was sent to, until its order starts.
	private static readonly Dictionary<Labor, Vector3> Plans = new Dictionary<Labor, Vector3>();

	private static readonly List<Place> Places = new List<Place>();

	private sealed class Anchor
	{
		internal Vector3 Home;

		internal Vector3 Sent;
	}

	private static readonly Dictionary<Labor, Anchor> Anchors = new Dictionary<Labor, Anchor>();

	private static readonly Dictionary<XYZ, int> Steps = new Dictionary<XYZ, int>();

	private static readonly Queue<TerrainPathNode> Open = new Queue<TerrainPathNode>();



	public static readonly CombatInstruction<GameObject> GoShoot = new CombatInstruction<GameObject>("RangedReposition", IconKeys.Archer, (Labor labor, GameObject enemy) => GoShootNode(labor, enemy));

	private static void Enable()
	{
		MaxDistance = Plugin.Cfg.Bind("RangedReposition", "MaxDistance", 8, new ConfigDescription("How far, in blocks, a ranged fighter whose shots would hit the castle or rock may go to find a place with a clear shot.", new AcceptableValueRange<int>(1, 20)));
		GameSession.OnLeave(() =>
		{
			NextSearch.Clear();
			Plans.Clear();
			Anchors.Clear();
		});
	}

	// Whether the fighter has a place near it with a clear shot at the enemy; it is kept for the order.
	internal static bool TryPlan(Labor labor, GameObject enemy)
	{
		float now = Time.time;
		if (NextSearch.TryGetValue(labor, out float next) && now < next)
		{
			return false;
		}
		NextSearch[labor] = now + RetrySeconds;
		Vector3 anchor = AnchorOf(labor);
		if (!FindPlace(labor, enemy, anchor, out Vector3 stand))
		{
			return false;
		}
		Plans[labor] = stand;
		Anchors[labor] = new Anchor { Home = anchor, Sent = stand };
		NextSearch.Remove(labor);
		return true;
	}

	// Where the fighter's moves are kept round: where it stood before this first moved it. A fighter that is no longer
	// on the place it was sent to (moved by the player or a task) gets a new anchor where it stands.
	private static Vector3 AnchorOf(Labor labor)
	{
		Vector3 at = labor.transform.position;
		if (Anchors.TryGetValue(labor, out Anchor anchor) && (at - anchor.Sent).sqrMagnitude <= 2.25f)
		{
			return anchor.Home;
		}
		Anchors.Remove(labor);
		return at;
	}

	// The places round the anchor to try, nearest walk first: walked out from the anchor's place along the route graph's
	// own links (so a place is only as far as the walk there; one beside it on the map but reached only down the
	// stairs and round is not near), by plain steps only (no ladder, forest or breaking step), never more than
	// MaxDrop below or MaxRise above it nor MaxDistance out from it.
	private static void CollectPlaces(Labor labor, Vector3 anchor)
	{
		Faction faction = labor.faction;
		bool useMap = ReachMap.Has(faction);
		int radius = MaxDistance.Value;
		XYZ start = XYZ.FromVector3(anchor);
		TerrainPathNode first = PathNodeRegistry.At(start) as TerrainPathNode;
		if (first == null || first.IsEmpty)
		{
			start = new XYZ(start.x, start.y + 1, start.z);
			first = PathNodeRegistry.At(start) as TerrainPathNode;
			if (first == null || first.IsEmpty)
			{
				return;
			}
		}
		Steps.Clear();
		Open.Clear();
		Steps[first.position] = 0;
		Open.Enqueue(first);
		while (Open.Count > 0)
		{
			TerrainPathNode node = Open.Dequeue();
			int steps = Steps[node.position];
			if (steps >= radius * 2 || node.siblings == null)
			{
				continue;
			}
			foreach (PathBranch branch in node.siblings)
			{
				if (!PlainStep(branch.Type) || !(branch.Node is TerrainPathNode next) || next.IsEmpty)
				{
					continue;
				}
				XYZ cell = next.position;
				int dx = cell.x - start.x;
				int dz = cell.z - start.z;
				if (cell.y < start.y - MaxDrop || cell.y > start.y + MaxRise || dx * dx + dz * dz > radius * radius)
				{
					continue;
				}
				if (Steps.ContainsKey(cell) || (useMap && !ReachMap.Contains(faction, cell)))
				{
					continue;
				}
				Steps[cell] = steps + 1;
				Open.Enqueue(next);
				Places.Add(new Place
				{
					Cell = cell,
					Stand = new Vector3(cell.x, next.GetSurfaceHeight(0f, 0f), cell.z),
					Distance = steps + 1
				});
			}
		}
		Steps.Clear();
		Open.Clear();
	}

	// A walk, a step up or down; not a ladder, a forest step or breaking through.
	private static bool PlainStep(PathBranch.BranchType type)
	{
		return type != PathBranch.BranchType.Ladder && type != PathBranch.BranchType.Forest && type != PathBranch.BranchType.Breaking;
	}

	private static bool FindPlace(Labor labor, GameObject enemy, Vector3 anchor, out Vector3 stand)
	{
		stand = Vector3.zero;
		Transform transform = labor.transform;
		Vector3 home = transform.position;
		Places.Clear();
		CollectPlaces(labor, anchor);
		// Where it stands now was tested already (the shot is stopped there).
		XYZ here = XYZ.FromVector3(home);
		Places.RemoveAll((Place p) => p.Cell.x == here.x && p.Cell.z == here.z && Mathf.Abs(p.Cell.y - here.y) <= 1);
		// Same height as the fighter first (it stays on the wall walk), and there the edges facing the enemy (where a
		// shot down is clear, leaning out: WallLean); then the nearest walk.
		float height = anchor.y;
		Vector3 towards = enemy.transform.position - anchor;
		XYZ facing = (Mathf.Abs(towards.x) >= Mathf.Abs(towards.z)) ? new XYZ((towards.x >= 0f) ? 1 : -1, 0, 0) : new XYZ(0, 0, (towards.z >= 0f) ? 1 : -1);
		Places.Sort((Place a, Place b) =>
		{
			int order = Rank(a, height, facing).CompareTo(Rank(b, height, facing));
			return (order != 0) ? order : a.Distance.CompareTo(b.Distance);
		});
		Location target = Location.Of(enemy);
		Quaternion rotation = transform.rotation;
		bool found = false;
		try
		{
			for (int i = 0; i < Places.Count && i < MaxTests; i++)
			{
				transform.position = Places[i].Stand;
				if (labor.Profession.CanFireAt(target))
				{
					stand = Places[i].Stand;
					found = true;
					break;
				}
			}
		}
		finally
		{
			transform.position = home;
			transform.rotation = rotation;
			Places.Clear();
		}
		return found;
	}

	// 0: same height, at an edge facing the enemy; 1: same height; 2: an edge a block up or down; 3: the rest.
	private static int Rank(Place place, float height, XYZ facing)
	{
		XYZ front = new XYZ(place.Cell.x + facing.x, place.Cell.y, place.Cell.z + facing.z);
		bool edge = Voxel.IsEmpty(front) && Voxel.IsEmpty(new XYZ(front.x, front.y - 1, front.z));
		bool level = Mathf.Abs(place.Stand.y - height) < 0.6f;
		return level ? (edge ? 0 : 1) : (edge ? 2 : 3);
	}

	// Walks to the place, then holds it and shoots at the enemy (or what comes into range there).
	private static Node GoShootNode(Labor labor, GameObject enemy)
	{
		if (!Plans.TryGetValue(labor, out Vector3 stand) || enemy.IsNullOrReleased())
		{
			Plans.Remove(labor);
			return Node.Empty;
		}
		Plans.Remove(labor);
		return (StaticNode)Node.Sequence.Label("Go to a place with a clear shot")
			.Do((Labor l) =>
			{
				// Fighting: the instinct does not stop the walk to decide again.
				l.Activity = Activity.Fighting;
			}, labor)
			.Do((Labor l, Vector3 at) => l.RunToExactly(new SpaceLocation(at, Quaternion.identity)), labor, stand)
			.Do((Labor l, GameObject e) => e.IsNullOrReleased() ? Node.Empty : (Node)l.HoldPositionAndAttackWithPriority(e), labor, enemy);
	}
}

// A ranged fighter whose shots at the enemy would be stopped looks for a place near it to shoot from, before it
// stands and waits or runs out to the enemy. Only on players' teams: the enemy waves keep the game's behaviour.
[Feature(Features.RangedReposition, Features.RangedRepositionInfo)]
[HarmonyPatch(typeof(LaborInstinct), "GetPackage")]
internal static class RangedRepositionPatch
{
	private static bool Prefix(Labor ___labor, GameObject enemy, LaborInstinct.EnemyProximity isClose, bool outnumbered, ref OrderPackage<Labor> __result)
	{
		if (enemy == null || outnumbered || isClose == LaborInstinct.EnemyProximity.Close || ___labor.IsNullOrReleased())
		{
			return true;
		}
		if (___labor.faction == null || ___labor.faction.isAI)
		{
			return true;
		}
		Profession profession = ___labor.Profession;
		if (profession == null || !profession.ProjectileAttackAvailable() || !___labor.Autonomy.ListenToRangeInstinct)
		{
			return true;
		}
		if (isClose == LaborInstinct.EnemyProximity.ExtendedRange && profession.IsStrongInMelee())
		{
			return true;
		}
		Location target = Location.Of(enemy);
		if (profession.CanFireAt(target) || !RangedReposition.TryPlan(___labor, enemy))
		{
			return true;
		}
		__result = Order.Package.OfGameObject(RangedReposition.GoShoot, enemy, Labor.DecisionSource.Character);
		return false;
	}
}

// A ranged fighter of a player's team (not strong in melee) that cannot shoot the enemy now and found no place to
// shoot from (RangedReposition) was sent running at the enemy (the game's GoAttack), down off the wall and into the
// fight. It now holds its place and shoots when it can (melee only if the enemy reaches it).
[Feature(Features.RangedReposition, Features.RangedRepositionInfo)]
[HarmonyPatch(typeof(LaborInstinct), "GetPackage")]
internal static class RangedHoldPatch
{
	[HarmonyPriority(Priority.Last)]
	private static void Postfix(Labor ___labor, GameObject enemy, ref OrderPackage<Labor> __result)
	{
		if (enemy == null || ___labor.IsNullOrReleased() || ___labor.faction == null || ___labor.faction.isAI || !(__result is OrderPackage<Labor, GameObject> package))
		{
			return;
		}
		if (package.InstructionValue != Order.Combat.GoAttack && package.InstructionValue != Order.Combat.GoAttackWithPriority)
		{
			return;
		}
		Profession profession = ___labor.Profession;
		if (profession == null || profession.IsStrongInMelee() || profession.toolbag == null)
		{
			return;
		}
		if (!profession.toolbag.HasToolFor(Adjectif.rangeDamage, out _) && !profession.toolbag.HasToolFor(Adjectif.rangePrecision, out _))
		{
			return;
		}
		__result = Order.Package.OfGameObject(Order.Combat.HoldPositionAndAttackWithPriority, enemy, Labor.DecisionSource.Character);
	}
}
