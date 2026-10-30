using System.Collections;
using System.Collections.Generic;
using Brix.Engine.FlatWorld;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Memory;

// The distant terrain (LOD) is built by LodUpdater from lower-detail copies of the megavoxels. Their pixel arrays come
// from a static pool keyed by array size and go back to it when a level is done, and the pool never shrinks. The
// first build after loading makes every LOD of the whole map at once, so the pool then keeps that peak (the heap scan
// found some 54 MB) for the rest of the game, while the later updates (terrain changed near a few megavoxels) need a
// small part of it.
//
// After a large build the pool is trimmed to a budget, keeping its mix of sizes. Small updates never trim, so the
// pool keeps what they need and they do not allocate again each time. The arrays hold only pixel data; what is
// drawn is the same.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(LodUpdater), "Update")]
internal static class LodPoolTrim
{
	// A build of at least this many dirty megavoxels counts as large (the first build after loading covers the map).
	private const int LargeBuild = 64;

	private const long BudgetBytes = 8L * 1024 * 1024;

	private const int Color32Bytes = 4;

	private static int _dirtyBefore;

	// The build Update started, and whether it was large. A build Update did not start (StartWorking, used for the
	// first build while the game loads) counts as large.
	private static IEnumerator _started;

	private static bool _startedLarge;

	private static void Prefix(LodUpdater __instance, out IEnumerator __state)
	{
		__state = __instance._work;
		_dirtyBefore = __instance._dirtyMvs.Count;
	}

	private static void Postfix(LodUpdater __instance, IEnumerator __state)
	{
		IEnumerator work = __instance._work;
		if (__state == null && work != null)
		{
			_started = work;
			_startedLarge = _dirtyBefore >= LargeBuild;
			return;
		}
		if (__state != null && work == null)
		{
			bool large = !ReferenceEquals(__state, _started) || _startedLarge;
			_started = null;
			if (large)
			{
				Trim();
			}
		}
	}

	private static void Trim()
	{
		Dictionary<int, LodUpdater.Pool<Color32[]>> pools = LodUpdater.ArrayPool<Color32>._pools;
		long total = 0;
		foreach (KeyValuePair<int, LodUpdater.Pool<Color32[]>> pair in pools)
		{
			total += (long)pair.Key * Color32Bytes * pair.Value._queue.Count;
		}
		if (total <= BudgetBytes)
		{
			return;
		}
		double keepShare = (double)BudgetBytes / total;
		long freed = 0;
		foreach (KeyValuePair<int, LodUpdater.Pool<Color32[]>> pair in pools)
		{
			Queue<Color32[]> queue = pair.Value._queue;
			int keep = (int)(queue.Count * keepShare);
			int drop = queue.Count - keep;
			if (drop <= 0)
			{
				continue;
			}
			for (int i = 0; i < drop; i++)
			{
				queue.Dequeue();
			}
			queue.TrimExcess();
			freed += (long)pair.Key * Color32Bytes * drop;
		}
		Plugin.Log.LogInfo("GameLeakFixes: trimmed the LOD array pool by " + (freed / (1024 * 1024)) + " MB after a large LOD build");
	}
}

// LodUpdater.Pool.ClearAll (run when a world is cleared or its LodUpdater destroyed) emptied every pool and then also
// forgot them, although they are static and live on: from the second world of the session the array pool and the
// pooled drawers (which reference the old world) were never emptied again, so the previous world's LOD arrays stayed
// in memory in the menu and in the next game. The pools are now emptied and kept registered.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(LodUpdater.Pool), nameof(LodUpdater.Pool.ClearAll))]
internal static class LodPoolClearAll
{
	private static bool Prefix()
	{
		List<LodUpdater.Pool> pools = LodUpdater.Pool._pools;
		for (int i = 0; i < pools.Count; i++)
		{
			pools[i].Clear();
		}
		return false;
	}
}
