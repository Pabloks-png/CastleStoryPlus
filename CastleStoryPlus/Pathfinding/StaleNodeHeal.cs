using Brix.Engine;
using Brix.Pathfinding;
using Brix.Pathfinding.Test;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Pathfinding;

// The route graph is rebuilt only round the voxels the terrain signals as changed. A node could be left standing in a
// voxel that has since become solid (seen at a quarry's rim: a node at the height of the old surface, the voxel full
// ground). Routes then led into it, the game's PopOuterOfObstacle found the bricktron inside the ground and pushed it
// to the next free cell, marking its path blocked, and the next route led straight back: the bricktron jumped between
// the two cells and never got to its stockpile or dig. Until the game was restarted (the graph is built fresh on load)
// every route over that cell failed. When a bricktron is pushed out of a voxel that still holds a node, the voxel is
// now handed to the graph as changed, so the node is rebuilt from the terrain as it is.
[Feature(Features.Pathfinding, Features.PathfindingInfo)]
[HarmonyPatch(typeof(PopOuterOfObstacle), nameof(PopOuterOfObstacle.PopOut))]
internal static class StaleNodeHeal
{
	private static void Prefix(PopOuterOfObstacle __instance)
	{
		TestPathfinding pathfinding = TestPathfinding.Instance;
		if (pathfinding == null || __instance == null || __instance.locomotion == null)
		{
			return;
		}
		XYZ cell = XYZ.FromVector3(__instance.locomotion.transform.position);
		if (!pathfinding.TerrainNodes.TryGetValue(cell, out TerrainPathNode node) || node == null || node.IsEmpty || Voxel.IsOpen(cell))
		{
			return;
		}
		pathfinding.DirtyVoxels.Add(cell);
		Plugin.Log.LogInfo("Pathfinding: route node at " + cell + " is inside solid ground (" + __instance.name + " was pushed out of it); rebuilding it");
	}
}
