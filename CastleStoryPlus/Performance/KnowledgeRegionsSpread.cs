using System.Collections.Generic;
using System.Reflection.Emit;
using Brix.Game.AI.KnowledgeSpace;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Performance;

// Every 4 game seconds the game works out which buildings and items each region of the map can reach (what workers
// may fetch and use). Its last step and the copy of the result into the knowledge the workers read ran in the same
// AI tick (about 2.3 + 1.4 ms), and the first step looked at the roots of 100 volumes a tick (about 0.9 ms). The copy
// now runs in the next fixed update (the result does not change before the next pass, 4 s later; the workers read
// the previous pass's knowledge one tick longer), and a tick takes 40 roots.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch]
internal static class KnowledgeRegionsSpread
{
	private const int RootsPerTick = 40;

	private static readonly List<KnowledgeUpdater> Pending = new List<KnowledgeUpdater>();

	private static bool _running;

	[HarmonyPatch(typeof(KnowledgeUpdater), "UpdateRegions")]
	[HarmonyPrefix]
	private static bool Defer(KnowledgeUpdater __instance)
	{
		if (_running)
		{
			return true;
		}
		if (!Pending.Contains(__instance))
		{
			Pending.Add(__instance);
		}
		return false;
	}

	// From FrameSaversDriver.FixedUpdate.
	internal static void RunPending()
	{
		if (Pending.Count == 0)
		{
			return;
		}
		_running = true;
		try
		{
			foreach (KnowledgeUpdater updater in Pending)
			{
				if (updater != null)
				{
					updater.UpdateRegions();
				}
			}
		}
		finally
		{
			Pending.Clear();
			_running = false;
		}
	}

	internal static void Clear()
	{
		Pending.Clear();
	}

	[HarmonyPatch(typeof(KnowledgeDisjointRegionsFinder), "AddRootsStep")]
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> FewerRoots(IEnumerable<CodeInstruction> instructions)
	{
		bool done = false;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!done && (instruction.opcode == OpCodes.Ldc_I4_S || instruction.opcode == OpCodes.Ldc_I4) && System.Convert.ToInt32(instruction.operand) == 100)
			{
				instruction.opcode = OpCodes.Ldc_I4_S;
				instruction.operand = (sbyte)RootsPerTick;
				done = true;
			}
			yield return instruction;
		}
		if (!done)
		{
			Plugin.Log.LogWarning("KnowledgeRegionsSpread: the 100 roots a tick were not found in AddRootsStep; left as it is");
		}
	}
}
