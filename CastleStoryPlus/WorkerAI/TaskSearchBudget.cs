using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;
using Motus.Behavior;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// A worker looking for its next task checks the tasks of its project one by one and paused a tick whenever the
// game's estimated cost passed 1 unit. Each dig/mine voxel, tree or boulder is its own task costing 0.1-0.2
// units, so only 5-10 were checked per tick: in a big mining area with hundreds of voxels a worker stood idle
// for seconds after every block. The checks are cheap, so the pause now follows real time instead: all
// searches together get FrameBudgetMs of CPU time per frame, then continue next frame.
[Feature(Features.FasterTaskSearch, Features.FasterTaskSearchInfo)]
[HarmonyPatch(typeof(GoalSelector), nameof(GoalSelector.CheckEnoughTimeConsumed))]
internal static class TaskSearchBudget
{
	private const double FrameBudgetMs = 3.0;

	private static readonly Stopwatch Clock = Stopwatch.StartNew();

	private static int _frame = -1;

	private static double _frameStartMs;

	private static bool Prefix(GoalSelector.GoalSelectionData selectionData, ref bool __result)
	{
		double now = Clock.Elapsed.TotalMilliseconds;
		if (Time.frameCount != _frame)
		{
			_frame = Time.frameCount;
			_frameStartMs = now;
		}
		__result = now - _frameStartMs > FrameBudgetMs;
		if (__result)
		{
			selectionData.timeSkipped = selectionData.timeConsumed;
		}
		return false;
	}
}

// The search's other waits. Before asking a project for a task the worker waited 1 to several ticks, more the more
// workers had searched just before (spreading the searches over time), and while checking that the project's
// resources can be fetched and its yield stored it waited a tick per kind of resource. Asking every project in turn,
// that added up to over a second after each task, for every kind of task. Now the worker asks at once and checks all
// kinds in one go; the goal search itself still keeps to its time budget per frame (TaskSearchBudget).
[Feature(Features.FasterTaskSearch, Features.FasterTaskSearchInfo)]
[HarmonyPatch(typeof(Labor), "OffsetWaitTime", MethodType.Getter)]
internal static class TaskSearchOffsetPatch
{
	private static void Postfix(ref int __result)
	{
		__result = 1;
	}
}

[Feature(Features.FasterTaskSearch, Features.FasterTaskSearchInfo)]
[HarmonyPatch]
internal static class TaskSearchVerifyPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(Project), "VerifyOneStorageFor");
		yield return AccessTools.Method(typeof(Project), "VerifyStorageForAll");
		yield return AccessTools.Method(typeof(Project), "VerifyResourceAccessFor");
	}

	// "Node.WaitForTicks(1)" becomes "Node.Empty".
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo wait = AccessTools.Method(typeof(Node), nameof(Node.WaitForTicks), new[] { typeof(int) });
		MethodInfo empty = AccessTools.PropertyGetter(typeof(Node), nameof(Node.Empty));
		List<CodeInstruction> code = new List<CodeInstruction>(instructions);
		int replaced = 0;
		for (int i = 0; i < code.Count - 1; i++)
		{
			if (code[i].LoadsConstant(1) && code[i + 1].Calls(wait))
			{
				code[i].opcode = OpCodes.Nop;
				code[i].operand = null;
				code[i + 1] = new CodeInstruction(OpCodes.Call, empty).MoveLabelsFrom(code[i + 1]);
				replaced++;
			}
		}
		if (replaced == 0)
		{
			Plugin.Log.LogWarning("FasterTaskSearch: no tick wait found to take out of a project's resource check");
		}
		return code;
	}
}
