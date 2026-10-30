using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// The game sends its statistics (GameAnalytics) to a server that no longer answers. Every 10 seconds it tried to send
// its queue, the request failed ("GA Error: Cannot connect to destination host"), and the failed events stayed queued
// to go again with the next ones: every other attempt made some 21 MB of garbage in one frame, growing the longer the
// game ran. Nothing is queued or sent any more.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(GA_Queue), nameof(GA_Queue.AddItem))]
internal static class AnalyticsOff
{
	private static bool Prefix()
	{
		return false;
	}
}

// AnalyticsOff: no submission of the (empty) queue either.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(GA_Queue), nameof(GA_Queue.ForceSubmit))]
internal static class AnalyticsSubmitOff
{
	private static bool Prefix()
	{
		return false;
	}
}
