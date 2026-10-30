using Brix.Game.AI;
using Brix.Pathfinding;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Pathfinding;

// Every slice of a route search first takes the searching unit out of its own node's obstacle count (and puts it back
// after), looking the unit's voxel up in TestPathfinding.VoxelPresences. The game never fills that dictionary, so the
// voxel was always (0, 0, 0) and the lookup walked the whole column at x 0, z 0, about 512 node lookups for every slice
// of every search, to find nothing (or, with ground there, to make a node at the map's corner count one obstacle less
// during the slice). With the dictionary empty the slice now skips it.
[Feature(Features.Pathfinding, Features.PathfindingInfo)]
[HarmonyPatch(typeof(SearchPathRequest), "RemoveVolumeTemporarily")]
internal static class SearchOwnVolume
{
	private static bool Prefix(SearchPathRequest __instance, ref PathNode __result)
	{
		if (__instance.TestPathfinding == null || __instance.TestPathfinding.VoxelPresences.Count > 0)
		{
			return true;
		}
		__result = PathNode.Empty;
		return false;
	}
}
