using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Combat;

// A worker without a soldier's gear (a builder: no sword and shield, no bow) went to fight any enemy that came close
// on its own, the game's instinct for every bricktron that has a melee attack (its bare hands count), and was cut
// down. Now the player's workers leave enemies to the soldiers: their instinct no longer sends them to fight. An
// order from the player to attack still does (that is not the instinct), and so does a worker that takes up a
// soldier's gear (its class changes). The enemy waves keep the game's behaviour.
[Feature(Features.UnarmedWorkers, Features.UnarmedWorkersInfo)]
[HarmonyPatch(typeof(Labor), "HasToFight")]
internal static class UnarmedWorkersPatch
{
	private static bool Prefix(Labor __instance, ref bool __result)
	{
		if (!IsUnarmedWorker(__instance))
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static bool IsUnarmedWorker(Labor labor)
	{
		if (labor == null || labor.faction == null || labor.faction.isAI || labor.Profession == null || labor.Occupation == null)
		{
			return false;
		}
		return labor.Occupation.CurrentOccupation == Occupation.Type.Builder && !labor.Profession.ProjectileAttackAvailable();
	}
}
