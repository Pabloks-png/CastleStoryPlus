using BepInEx.Configuration;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.UI;

// The game's ward range (WardRadiusRenderer: a camera effect that tints the spheres of the wards, pylons and crystals
// around a hovered or placed ward) is drawn in the ward's full colour, so the ground under it is hard to see. Its
// colour is handed to the effect at [WardRange] Strength instead (1 = as the game draws it).
[Feature(Features.WardRangeFade, Features.WardRangeFadeInfo)]
[HarmonyPatch(typeof(WardRadiusRenderer), "UpdateProperties")]
internal static class WardRangeFade
{
	private static readonly AccessTools.FieldRef<WardRadiusRenderer, Material> Material = AccessTools.FieldRefAccess<WardRadiusRenderer, Material>("_camEffectMat");

	private static readonly AccessTools.FieldRef<WardRadiusRenderer, Color> WardColor = AccessTools.FieldRefAccess<WardRadiusRenderer, Color>("_wardColor");

	private static readonly int HighlightColor = Shader.PropertyToID("_HighlightColor");

	private static ConfigEntry<float> _strength;

	private static void Enable()
	{
		_strength = Plugin.Cfg.Bind("WardRange", "Strength", 0.35f, "How strongly the ward range spheres are drawn (0.05 to 1; 1 = as the game draws them).");
	}

	private static void Postfix(WardRadiusRenderer __instance)
	{
		Material material = Material(__instance);
		if (material == null || _strength == null)
		{
			return;
		}
		float strength = Mathf.Clamp(_strength.Value, 0.05f, 1f);
		Color color = WardColor(__instance);
		material.SetColor(HighlightColor, new Color(color.r * strength, color.g * strength, color.b * strength, color.a * strength));
	}
}
