using Brix.Engine.Nature;
using Brix.External.Factories;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Metallurgy;

// Coal deposits have their own switch in a harvest task, next to stone, iron, brimstone, trees and plants. A coal
// deposit is an iron deposit to the game (CoalDeposits), so a task took coal whenever it took iron and coal could not
// be chosen on its own. A coal deposit's goal now has a type of its own (Coal), and the task's filter carries it.
//
// The task's choice is kept in its own saved and synced number of accepted types (HarvestData.acceptedTypes, the
// game's nature type bits 1 to 16), in two bits the game does not use: Explicit (the task has its own coal setting) and
// CoalOn. A task without Explicit (every older task, and one made with the game's tools) takes coal with iron, as
// before. The game's beacon icon reads only its own bits.
[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
internal static class CoalHarvest
{
	// A coal deposit's goal type, and coal in the goal provider's filter.
	internal const NatureType Coal = (NatureType)32;

	private const int GameTypes = (int)NatureType.everything;

	private const int Explicit = 64;

	private const int CoalOn = 128;

	// Whether a task with these accepted types takes coal.
	internal static bool Accepts(int accepted)
	{
		return ((accepted & Explicit) != 0) ? ((accepted & CoalOn) != 0) : ((accepted & (int)NatureType.iron) != 0);
	}

	// The accepted types with coal switched over.
	internal static int Toggled(int accepted)
	{
		return (accepted & ~CoalOn) | Explicit | (Accepts(accepted) ? 0 : CoalOn);
	}

	[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
	[HarmonyPatch(typeof(NatureHarvestGoal), nameof(NatureHarvestGoal.NatureType), MethodType.Getter)]
	private static class GoalType
	{
		private static void Postfix(NatureHarvestGoal __instance, ref NatureType __result)
		{
			if (__result == NatureType.iron && CoalDeposits.IsCoal(__instance.NData))
			{
				__result = Coal;
			}
		}
	}

	[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
	[HarmonyPatch(typeof(HarvestGoalProvider), nameof(HarvestGoalProvider.SetAcceptedTypes))]
	private static class Filter
	{
		private static void Prefix(ref NatureType natureType)
		{
			int accepted = (int)natureType;
			natureType = (NatureType)((accepted & GameTypes) | (Accepts(accepted) ? (int)Coal : 0));
		}
	}

	[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
	[HarmonyPatch(typeof(HarvestData), nameof(HarvestData.GetHarvestMeta))]
	private static class Beacon
	{
		private static void Prefix(ref NatureType type)
		{
			type = (NatureType)((int)type & GameTypes);
		}
	}
}
