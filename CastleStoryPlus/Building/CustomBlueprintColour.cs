using System.Collections.Generic;
using Brix.External.Factories;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// The game colours a blueprint by its state through its StateRenderer: red when it cannot be placed or built (no
// support, obstructed). The mod's buildings (market, smithy, warehouse, ...) draw their blueprint with their own model
// and one fixed see-through material, which the StateRenderer does not know, so they stayed blue when invalid: while
// placing and after the ground under them changed, and workers then ignored them without a sign why. Now their model
// turns the game's red while the blueprint is not valid and back to its own material when it is.
[Feature]
[HarmonyPatch(typeof(Blueprint), "StateChange")]
internal static class CustomBlueprintColour
{
	// Blueprint key -> name of the model's root under the blueprint (see CustomBuilding).
	private static readonly Dictionary<Factory.AssetKey, string> Models = new Dictionary<Factory.AssetKey, string>();

	// The model's own material -> its red copy.
	private static readonly Dictionary<Material, Material> Reds = new Dictionary<Material, Material>();

	// A red copy -> the model's own material, to put back.
	private static readonly Dictionary<Material, Material> Originals = new Dictionary<Material, Material>();

	internal static void Register(Factory.AssetKey key, string modelName)
	{
		Models[key] = modelName;
	}

	private static void Postfix(Blueprint __instance, PlacementFlags newMode)
	{
		if (__instance == null || Models.Count == 0 || !Models.TryGetValue(__instance.AssetKey, out string modelName))
		{
			return;
		}
		Transform model = __instance.transform.Find(modelName);
		if (model == null)
		{
			return;
		}
		bool invalid = (newMode & (PlacementFlags.valid | PlacementFlags.anti | PlacementFlags.mayRemove)) == 0;
		foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			Material current = renderer.sharedMaterial;
			if (current == null)
			{
				continue;
			}
			if (!Originals.TryGetValue(current, out Material original))
			{
				original = current;
			}
			Material wanted = invalid ? RedOf(original) : original;
			if (current != wanted)
			{
				renderer.sharedMaterial = wanted;
			}
		}
	}

	private static Material RedOf(Material original)
	{
		if (!Reds.TryGetValue(original, out Material red))
		{
			red = new Material(original);
			red.name = original.name + " (invalid)";
			red.color = InvalidColour(original.color.a);
			Reds[original] = red;
			Originals[red] = original;
		}
		return red;
	}

	// The red the game gives an invalid blueprint (the picking display's colour), with the model's own transparency.
	private static Color InvalidColour(float alpha)
	{
		Color red = new Color(1f, 0.15f, 0.1f, alpha);
		try
		{
			BlueprintMaterialCache.BlueprintDisplay display = BlueprintMaterialCache.ForMode(PlacementFlags.none);
			if (display != null)
			{
				red = new Color(display.color.r, display.color.g, display.color.b, alpha);
			}
		}
		catch (System.Exception)
		{
			// The cache is not up yet: keep the fallback red.
		}
		return red;
	}
}
