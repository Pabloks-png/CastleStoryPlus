using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Treasure;

// The treasure's castle ruins (and the chests in them) show through the fog of war, as the ground does: the game
// hides every object of another faction where its faction cannot see, and the ruins' blocks are the neutral
// faction's. Neutral objects that are not units are seen by everyone; neutral units and the other factions' objects
// stay hidden in the fog.
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch(typeof(LocalFactionVision), nameof(LocalFactionVision.IsVisibleForFaction))]
internal static class RuinsInFog
{
	private static void Postfix(LocalFactionVision __instance, ref bool __result)
	{
		if (__result || __instance == null)
		{
			return;
		}
		Faction neutral = Faction.DefaultNeutral;
		if (neutral != null && __instance.faction == neutral && __instance.GetComponent<Labor>() == null)
		{
			__result = true;
		}
	}
}
