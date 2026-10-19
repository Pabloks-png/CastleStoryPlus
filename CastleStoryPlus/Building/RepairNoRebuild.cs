using System.Collections.Generic;
using Brix.Engine;
using Brix.Engine.Blocks;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// A repair area (RepairProject) puts a blueprint on every block removed inside it, to rebuild what an attack broke.
// It skips a block with another project's blueprint on it, but a deconstruction blueprint is gone before its block
// is (the removal is queued to the next voxel batch), so everything deconstructed inside a repair area was put back
// up as blueprints at once. Blocks taken down by a deconstruction are remembered for a moment and not rebuilt.
[Feature(Features.BuildJobs, Features.BuildJobsInfo)]
internal static class RepairNoRebuild
{
	// The queued removal lands within a few frames; a mark older than this is stale.
	private const float MarkSeconds = 10f;

	private static readonly Dictionary<XYZ, float> Deconstructed = new Dictionary<XYZ, float>();

	internal static void Mark(IVolume volume)
	{
		float now = Time.time;
		foreach (XYZ position in volume.blueprint)
		{
			Deconstructed[position] = now;
		}
	}

	// True (once) when the block at position was taken down by a deconstruction.
	internal static bool TakeMark(XYZ position)
	{
		if (!Deconstructed.TryGetValue(position, out float at))
		{
			return false;
		}
		Deconstructed.Remove(position);
		if (Deconstructed.Count > 256)
		{
			Prune();
		}
		return Time.time - at <= MarkSeconds;
	}

	private static void Prune()
	{
		List<XYZ> stale = new List<XYZ>();
		foreach (KeyValuePair<XYZ, float> mark in Deconstructed)
		{
			if (Time.time - mark.Value > MarkSeconds)
			{
				stale.Add(mark.Key);
			}
		}
		foreach (XYZ position in stale)
		{
			Deconstructed.Remove(position);
		}
	}
}

// A finished deconstruction blueprint (anti) marks the blocks it takes down.
[Feature(Features.BuildJobs, Features.BuildJobsInfo)]
[HarmonyPatch(typeof(Blueprint), nameof(Blueprint.MorphToObject))]
internal static class RepairNoRebuildMarkPatch
{
	private static void Prefix(Blueprint __instance)
	{
		if (!__instance.IsAnti)
		{
			return;
		}
		IVolume volume = __instance.GetComponent<IVolume>();
		if (volume != null)
		{
			RepairNoRebuild.Mark(volume);
		}
	}
}

[Feature(Features.BuildJobs, Features.BuildJobsInfo)]
[HarmonyPatch(typeof(PlaceBlueprintsOnBrokenBlocks), "OnBlockRemoved")]
internal static class RepairNoRebuildPatch
{
	private static bool Prefix(XYZ position)
	{
		return !RepairNoRebuild.TakeMark(position);
	}
}
