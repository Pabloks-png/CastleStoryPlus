#if UNITY6
using Brix.Engine;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Core;

// Unity 6: the new game screen could start no map. Selecting one threw in Minimap.GetTerrainBounds ("Texture2D.
// GetPixels32: invalid mipmap level"), so its inspector was never filled in (placeholder labels) and Play did
// nothing. The game reads the map's heightmap (Heightmap.png, loaded with LoadImage into a new Texture2D(0, 0)) at
// mipmap level 3; on 5.6 that texture got its mipmaps, on Unity 6 it has only the one level. Without the level the
// bounds are worked out from the full-size image instead: each pixel a column, red its bottom and green its top.
[Feature]
[HarmonyPatch(typeof(Minimap), nameof(Minimap.GetTerrainBounds))]
internal static class HeightmapBoundsFix
{
	private const int GameMipLevel = 3;

	private static bool Prefix(Texture2D tex, ref Bounds __result)
	{
		if (tex == null || tex.mipmapCount > GameMipLevel)
		{
			return true;
		}
		Color32[] pixels = tex.GetPixels32(0);
		int width = tex.width;
		XYZ min = new XYZ(1024, 256, 1024);
		XYZ max = XYZ.zero;
		for (int i = 0; i < pixels.Length; i++)
		{
			Color32 color = pixels[i];
			if (color.g > color.r)
			{
				int x = i % width;
				int z = i / width;
				min = XYZ.Min(min, new XYZ(x, color.r, z));
				max = XYZ.Max(max, new XYZ(x, color.g - 1, z));
			}
		}
		__result = new VoxelBounds(min, max).ToVectorBounds();
		return false;
	}
}
#endif
