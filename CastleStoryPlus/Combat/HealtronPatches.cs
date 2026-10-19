using System.Collections.Generic;
using System.Reflection;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Combat;

// Raids decide who is a Healtron as they hire their corruptrons (invasion waves, conquest and every mode with raids).
[Feature(Features.Healtron, Features.HealtronInfo)]
[HarmonyPatch(typeof(RaidProject), nameof(RaidProject.OnHire))]
internal static class HealtronHirePatch
{
	private static void Postfix(RaidProject __instance, Labor labor)
	{
		Healtron.OnRaidHire(__instance, labor);
	}
}

// A Healtron's own hits are weak. Applied to the attack's value before the game decides the reaction (as the
// armour upgrades are), so a hit that no longer kills does not play the death reaction. The receivers that do the
// damage themselves: units, objects (buildings, crystals) and placed blocks; the multi-block and proxy receivers
// pass the attack on to these, so it is scaled once. Server side, where attacks are resolved.
[Feature(Features.Healtron, Features.HealtronInfo)]
[HarmonyPatch]
internal static class HealtronDamagePatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(BricktronDamageReceiver), nameof(BricktronDamageReceiver.ReceiveAttack));
		yield return AccessTools.Method(typeof(ObjectDamageReceiver), nameof(ObjectDamageReceiver.ReceiveAttack));
		yield return AccessTools.Method(typeof(PlacedBlockDamageReceiver), nameof(PlacedBlockDamageReceiver.ReceiveAttack));
	}

	private static void Prefix(ref float value, GameObject attacker)
	{
		if (value > 0f && attacker != null && NetworkServer.active && Healtron.IsHealtron(attacker))
		{
			value *= Mathf.Max(0f, Healtron.DamageMultiplier.Value);
		}
	}
}

// A unit taken from the pool is no Healtron (a reused one's stats are dropped by WorkerStatsReset): its colours
// come back at once, and a raid that hires it may roll for it.
[Feature(Features.Healtron, Features.HealtronInfo)]
[HarmonyPatch(typeof(Labor), nameof(Labor.OnRequested))]
internal static class HealtronResetPatch
{
	private static void Prefix(Labor __instance)
	{
		Healtron.OnRequested(__instance);
	}
}
