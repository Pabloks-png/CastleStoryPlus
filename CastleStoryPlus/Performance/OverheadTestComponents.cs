using Brix.NewUI.OverheadDisplay;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Performance;

// Every overhead icon (over each bricktron, enemy, stockpile and building, some 300 in a base) carries two of the
// developers' test components, TestContainer and TestOrderQueue, whose Update only looks at check boxes meant for the
// inspector; nothing in the game sets them. Unity still called both every frame on every icon. Each switches itself
// off on its first update.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch]
internal static class OverheadTestComponents
{
	[HarmonyPatch(typeof(TestContainer), "Update")]
	[HarmonyPrefix]
	private static bool Container(TestContainer __instance)
	{
		__instance.enabled = false;
		return false;
	}

	[HarmonyPatch(typeof(TestOrderQueue), "Update")]
	[HarmonyPrefix]
	private static bool OrderQueue(TestOrderQueue __instance)
	{
		__instance.enabled = false;
		return false;
	}
}
