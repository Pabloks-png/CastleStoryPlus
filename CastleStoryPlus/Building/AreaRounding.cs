using Brix.Engine;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// A flatten area (and a tree farm, placed the same way) could only be put on every other block. Its blocks are found from its centre
// and size: the last block is the one at the far edge minus half a block. With an even width and the centre on a block
// (where the task's marker stands), that falls exactly half way between two blocks, and Unity rounds a half to the
// nearest even number, so moving the area by one block moved its blocks by 0 or 2. A half is now always rounded up.
[Feature]
[HarmonyPatch(typeof(VoxelBounds), nameof(VoxelBounds.FromVectorBoundsAdjustedToMax))]
internal static class AreaRounding
{
	private static bool Prefix(Bounds bounds, ref VoxelBounds __result)
	{
		Vector3 last = bounds.max - Vector3.one * 0.5f;
		XYZ max = new XYZ(Mathf.FloorToInt(last.x + 0.5f), Mathf.FloorToInt(last.y + 0.5f), Mathf.FloorToInt(last.z + 0.5f));
		XYZ size = XYZ.FromVector3(bounds.size);
		__result = new VoxelBounds(max - size + XYZ.one, max);
		return false;
	}
}
