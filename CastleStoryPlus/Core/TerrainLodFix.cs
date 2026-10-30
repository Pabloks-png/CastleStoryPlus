using System.Collections.Generic;
using Brix.Engine;
using Brix.Engine.FlatWorld;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Core;

// The distant terrain (the LOD levels) is redrawn in batches: a worker thread works out each coarse section's data
// (LodBatchDrawer.InitLodData, ending in LodDrawer.InitRender per section), then the main thread turns every section
// into a mesh (LodBatchDrawer.RenderLod, LodDrawer.Render). The main thread only waits for the thread to end, not for
// it to succeed: when the thread dies part way (an exception, e.g. from a timed method on the wrong thread), the
// sections it did not reach still hold arrays straight from the pool, sized for another section or not allocated
// at all, and Display.CountLocalizedMesh ran off the end of the section's config colours (an index out of range).
// That stopped the whole redraw and left the pooled arrays of the finer level unreturned. The game has the same
// flaw on 5.6; it only shows when the thread fails. A section is now drawn only once its data was finished; the
// others keep the mesh they already had until the next redraw, the rest of the batch is drawn as before.
[Feature]
[HarmonyPatch(typeof(LodUpdater.LodDrawer), nameof(LodUpdater.LodDrawer.InitRender))]
internal static class TerrainLodReadyPatch
{
	// Sections whose render data is complete, filled in on the worker thread and read on the main thread.
	private static readonly HashSet<IFlatWorldColors> Ready = new HashSet<IFlatWorldColors>();

	private static bool _warned;

	// Only reached when InitRender returned normally.
	private static void Postfix(LodUpdater.LodDrawer __instance)
	{
		IFlatWorldColors world = __instance.LowWorld;
		if (world == null)
		{
			return;
		}
		lock (Ready)
		{
			Ready.Add(world);
		}
	}

	internal static bool IsReady(IFlatWorldColors world)
	{
		bool ready;
		lock (Ready)
		{
			ready = world != null && Ready.Contains(world);
		}
		if (!ready && !_warned)
		{
			_warned = true;
			Plugin.Log.LogWarning("Terrain LOD: a section was not finished by the worker thread and was not redrawn (see the error before this)");
		}
		return ready;
	}

	// The sections of a finished batch (the drawn ones and the padding around them) are not needed any more.
	internal static void Forget(Dictionary<XYZ, FlatWorldColors> worlds)
	{
		if (worlds == null)
		{
			return;
		}
		lock (Ready)
		{
			foreach (FlatWorldColors world in worlds.Values)
			{
				Ready.Remove(world);
			}
		}
	}
}

[Feature]
[HarmonyPatch(typeof(LodUpdater.LodDrawer), nameof(LodUpdater.LodDrawer.Render))]
internal static class TerrainLodRenderPatch
{
	private static bool Prefix(LodUpdater.LodDrawer __instance)
	{
		return TerrainLodReadyPatch.IsReady(__instance.LowWorld);
	}
}

[Feature]
[HarmonyPatch(typeof(LodUpdater.LodBatchDrawer), nameof(LodUpdater.LodBatchDrawer.RenderLod))]
internal static class TerrainLodBatchPatch
{
	// A finalizer, so the batch is forgotten even when drawing it threw.
	private static void Finalizer(Dictionary<XYZ, FlatWorldColors> lowWorlds)
	{
		TerrainLodReadyPatch.Forget(lowWorlds);
	}
}

#if UNITY6
// Unity 6: digging a quarry flooded the log with "This Mesh Collider is attached to GameObject at path 'Jeu/
// Environnement/TerrainData (0)/Rendered/Lod 0/(x, y, z)' ... but the mesh doesn't have any vertices". A terrain
// section that holds no solid block (the air above a quarry, a section dug out) gets an empty collider mesh
// (TestWorldConfig3.RefreshCollider), and MegaVoxelGeo.RefreshCollider hands it to the section's MeshCollider. Unity
// 5.6 took the empty mesh without a word; Unity 6 reports it on every assignment and every re-enable. An empty mesh
// collides with nothing, the same as no mesh, so the collider is given no mesh instead. The section's visible mesh is
// untouched, and the next refresh with blocks in it assigns the mesh as before.
[Feature]
[HarmonyPatch(typeof(MegaVoxelGeo), nameof(MegaVoxelGeo.RefreshCollider))]
internal static class TerrainEmptyColliderFix
{
	private static bool Prefix(MegaVoxelGeo __instance)
	{
		if (!__instance.hasCollider)
		{
			return true;
		}
		Mesh mesh = __instance.colliderMesh != null ? __instance.colliderMesh : __instance.mesh;
		if (mesh == null || mesh.vertexCount > 0)
		{
			return true;
		}
		// A section without a collider yet gets none: the first refresh with blocks in it adds one, as the game does.
		if (__instance.meshCollider != null)
		{
			__instance.meshCollider.sharedMesh = null;
		}
		return false;
	}
}
#endif
