using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// A cleanup task's litter goals: every refresh (about 0.6 s) LitterGoalProvider.RemoveOldGoals adds the litter no
// longer there to a list (_toRemoveGO) and takes each of them out of its goal map, but never empties the list. It grew
// with every piece of litter ever picked up in the task's circle, and every refresh went through all of it again (a
// cleanup task's update reached several ms). A pooled piece of litter used again later had its new goal dropped on
// every refresh. The list is emptied once it has been used.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(LitterGoalProvider), "RemoveOldGoals")]
internal static class LitterGoalLeak
{
	private static void Postfix(LitterGoalProvider __instance)
	{
		__instance._toRemoveGO.Clear();
	}
}
