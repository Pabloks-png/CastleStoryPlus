using BepInEx.Configuration;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Nature;

// The fog of war lifts [FogOfWar] VisionMultiplier times as far around every unit that sees by the game's rule
// (bricktrons and the rest that use GeneralRules.FogOfWarDistance: 40, 60 or 80 by map size). The map's distance is
// scaled when read, so the value the game saves stays its own.
[Feature(Features.WiderVision, Features.WiderVisionInfo)]
[HarmonyPatch(typeof(LocalFactionVisionConfigurator), "FogDistance", MethodType.Getter)]
internal static class WiderVision
{
	internal static ConfigEntry<float> Multiplier;

	private static void Enable()
	{
		Multiplier = Plugin.Cfg.Bind("FogOfWar", "VisionMultiplier", 1.1f, new ConfigDescription("How far units see through the fog of war, times the game's distance for the map size.", new AcceptableValueRange<float>(0.5f, 3f)));
	}

	private static void Postfix(ref int __result)
	{
		if (Multiplier != null && __result > 0)
		{
			__result = UnityEngine.Mathf.RoundToInt(__result * Multiplier.Value);
		}
	}
}
