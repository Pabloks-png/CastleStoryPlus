using Brix.Engine;
using Brix.Engine.Blocks;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Pathfinding;

// A placed block told the navigation graph (and the structure check) about its root voxel only; the graph then
// rebuilt that voxel and the ones right around it. The game's blocks are at most 2 long, so that covered them, but
// the mod's long bricks and slabs (1x3, 1x4, 2x2, 2x4) reach 3 blocks from their root: their far end kept the nodes
// from before it was built. On a floor of slabs that left a strip with no nodes on top (an invisible wall: "nothing
// to do" next to open work) and old nodes inside the new block that routes still went through, so the walk failed
// on the way (PathExecutionFailed). Every voxel a block takes is now announced, as removing a block already did.
[Feature(Features.Pathfinding, Features.PathfindingInfo)]
[HarmonyPatch(typeof(BlockEngine), nameof(BlockEngine.RegisterBlock))]
internal static class BlockFootprintRefresh
{
	private static void Postfix(BlockEngine __instance, XYZ rootPosition)
	{
		IBlockData data = __instance.GetBlockData(rootPosition);
		IVolume volume = (data != null && data.RootOrSelf != null) ? data.RootOrSelf.Volume : null;
		if (volume == null)
		{
			return;
		}
		foreach (XYZ cell in volume.GetCases(VProperty.occupy))
		{
			if (cell != rootPosition)
			{
				GameSignals.Invoke(GameSignals.BlockTypeChanged, cell);
			}
		}
	}
}
