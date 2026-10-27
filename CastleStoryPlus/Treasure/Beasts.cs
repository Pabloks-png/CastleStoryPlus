using System.Collections.Generic;
using System.Reflection;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using Brix.Game.Locomotion;
using Brix.Transactions;
using CastleStoryPlus.Combat;
using CastleStoryPlus.Core;
using CastleStoryPlus.Giant;
using CastleStoryPlus.Workers;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Treasure;

// Beasts are ordinary enemy units (corruptrons, biftrons) marked with a mod variant (WorkerStats.Variant, saved and
// synced like a Healtron's): VariantBase + their kind. The flag decides their voxel model (BeastBody, every peer),
// their health, damage and speed (the patches below, where the game resolves them).
internal static class Beasts
{
	internal const int VariantBase = 10;

	private static readonly List<Labor> Units = new List<Labor>();

	// The beasts found by the last scan (alive or not).
	internal static List<Labor> All => Units;

	private static float _nextScan;

	internal static bool Dirty;

	internal static BeastInfo KindOf(Labor labor)
	{
		if (labor == null || labor.state == null)
		{
			return null;
		}
		WorkerStats stats = WorkerStats.Peek(labor.state);
		if (stats == null)
		{
			return null;
		}
		int kind = stats.Variant - VariantBase;
		return (kind >= 0 && kind < BeastKinds.All.Length) ? BeastKinds.All[kind] : null;
	}

	internal static BeastInfo KindOf(GameObject go)
	{
		return (go != null) ? KindOf(go.GetComponent<Labor>()) : null;
	}

	internal static bool IsAliveBeast(Labor labor)
	{
		return labor != null && labor.gameObject.activeInHierarchy && GiantBricktron.IsAlive(labor) && KindOf(labor) != null;
	}

	// Server: a beast of the kind, of the faction, standing at the position.
	internal static Labor Spawn(BeastKind kind, Faction faction, Vector3 at, int level)
	{
		BeastInfo info = BeastKinds.All[(int)kind];
		GameObject go = Transactor.SpawnUNet(info.Unit, faction, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
		if (go == null)
		{
			return null;
		}
		go.GetComponent<Occupation>().SetOccupation(info.Occupation, true);
		Profession profession = go.GetComponent<Profession>();
		if (profession != null)
		{
			profession.CharacterLevel = level;
		}
		Labor labor = go.GetComponent<Labor>();
		if (labor == null || labor.state == null)
		{
			return labor;
		}
		Healtron.MarkRolled(labor);
		WorkerStats.Modify(labor.state, (WorkerStats s) => s.Variant = VariantBase + (int)kind);
		WorkerStats.RaiseVariantChanged(labor.state);
		return labor;
	}

	// Every peer, every frame: living beasts without their model get it (made, received, loaded or back from the pool).
	internal static void Update()
	{
		if (!Dirty && Time.time < _nextScan)
		{
			return;
		}
		Dirty = false;
		_nextScan = Time.time + 1f;
		Live<Labor>.Active(Units);
		foreach (Labor labor in Units)
		{
			if (labor == null)
			{
				continue;
			}
			BeastInfo info = KindOf(labor);
			if (info != null && labor.gameObject.activeInHierarchy && GiantBricktron.IsAlive(labor))
			{
				BeastBody.Attach(labor.gameObject, info, false);
			}
			else if (info == null && labor.GetComponent<BeastBody>() != null)
			{
				BeastBody.DetachFrom(labor.gameObject);
			}
		}
	}

	// How fast a calm beast walks while it roams round its chest, relative to its speed when provoked.
	private const float CalmSpeed = 0.5f;

	internal static float SpeedFactor(Labor labor)
	{
		BeastInfo info = KindOf(labor);
		if (info == null)
		{
			return 1f;
		}
		return Mathf.Max(0.1f, info.Speed.Value) * (BeastTemper.IsCalm(labor) ? CalmSpeed : 1f);
	}
}

// A beast's hits do Damage times its base unit's (before the game decides the reaction). Server side.
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch]
internal static class BeastDamagePatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(BricktronDamageReceiver), nameof(BricktronDamageReceiver.ReceiveAttack));
		yield return AccessTools.Method(typeof(ObjectDamageReceiver), nameof(ObjectDamageReceiver.ReceiveAttack));
		yield return AccessTools.Method(typeof(PlacedBlockDamageReceiver), nameof(PlacedBlockDamageReceiver.ReceiveAttack));
	}

	private static void Prefix(ref float value, GameObject attacker)
	{
		if (value > 0f && attacker != null && NetworkServer.active)
		{
			BeastInfo info = Beasts.KindOf(attacker);
			if (info != null)
			{
				value *= Mathf.Max(0f, info.Damage.Value);
			}
		}
	}
}

// A beast takes 1/Health of the damage (healing is negative and unchanged).
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch(typeof(BricktronDamageReceiver), nameof(BricktronDamageReceiver.ApplyDamage), new[] { typeof(float), typeof(DamageType), typeof(DamageReaction), typeof(Vector3), typeof(Vector3), typeof(float), typeof(GameObject) })]
internal static class BeastHealthPatch
{
	private static void Prefix(BricktronDamageReceiver __instance, ref float value)
	{
		if (value > 0f)
		{
			BeastInfo info = Beasts.KindOf(__instance.Labor);
			if (info != null)
			{
				value /= Mathf.Max(0.01f, info.Health.Value);
			}
		}
	}
}

[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch]
internal static class BeastSpeedPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.PropertyGetter(typeof(Walking), "SpeedMperS");
		yield return AccessTools.PropertyGetter(typeof(Running), "SpeedMperS");
	}

	private static void Postfix(LocomotionWalkableCycleState __instance, ref float __result)
	{
		Locomotion4 locomotion = __instance.Locomotion;
		if (locomotion != null)
		{
			__result *= Beasts.SpeedFactor(locomotion.Labor);
		}
	}
}

// A dead beast's corpse shows the beast, fallen over.
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch(typeof(Corpse), nameof(Corpse.Spawn))]
internal static class BeastCorpsePatch
{
	private static void Postfix(GameObject original, Corpse __result)
	{
		if (original == null || __result == null)
		{
			return;
		}
		BeastInfo info = Beasts.KindOf(original);
		if (info != null)
		{
			BeastBody.Attach(__result.gameObject, info, true);
		}
	}
}

// A unit taken from the pool starts as an ordinary unit (its variant is dropped by WorkerStatsReset).
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch(typeof(Labor), nameof(Labor.OnRequested))]
internal static class BeastResetPatch
{
	private static void Prefix(Labor __instance)
	{
		BeastBody.DetachFrom(__instance.gameObject);
	}
}
