using System.Collections.Generic;
using System.Reflection.Emit;
using Brix.Assets;
using Brix.Engine.FlatWorld.Layers;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.WorldGen;

// Less earth over the stone in random worlds: the game fills a map's columns (HeightmapLayer) with the top voxel's
// type, earth in the 3 layers under the top (the top's own layer among them) and over the bottom, and stone (the
// internal type) in between. In a random world (a new one, and its saves, which keep its codename) the earth under
// the top is one layer: the grass, one layer of earth, then stone. The earth over the bottom is left as it is.
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch]
internal static class RandomWorldEarth
{
	private const int GameDepth = 3;

	private const int RandomWorldDepth = 2;

	[HarmonyPatch(typeof(HeightmapLayer), "GetTerrainData")]
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> TerrainData(IEnumerable<CodeInstruction> instructions)
	{
		return Shallower(instructions);
	}

	[HarmonyPatch(typeof(HeightmapLayer), nameof(HeightmapLayer.InitialData))]
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Initial(IEnumerable<CodeInstruction> instructions)
	{
		return Shallower(instructions);
	}

	// The top's "- 3" (a 3 followed by a subtraction) asks EarthDepth instead; the bottom's "+ 3" stays.
	private static IEnumerable<CodeInstruction> Shallower(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		int changed = 0;
		for (int i = 0; i < codes.Count - 1; i++)
		{
			if (codes[i].opcode == OpCodes.Ldc_I4_3 && codes[i + 1].opcode == OpCodes.Sub)
			{
				codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RandomWorldEarth), nameof(EarthDepth))).MoveLabelsFrom(codes[i]);
				changed++;
			}
		}
		if (changed != 1)
		{
			Plugin.Log.LogWarning("RandomWorld: expected one top earth depth in HeightmapLayer, found " + changed);
		}
		return codes;
	}

	public static int EarthDepth()
	{
		Asset_Map map = GameParam.Map;
		return (map != null && map.codename == RandomWorld.Codename) ? RandomWorldDepth : GameDepth;
	}
}
