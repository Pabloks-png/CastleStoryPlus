using System.Collections.Generic;
using Brix.Engine;
using Brix.Game.AI;
using Brix.Game.AI.UI;
using Brix.Input;
using Brix.Utils.Locomotion;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// The height handle of a flatten task (and a quarry's depth handle) jumped two blocks for one dragged: while dragging,
// the whole distance from where the drag started was added to the height at every step, and once more when the drag
// ended. The height now is the height at the start of the drag plus the distance dragged.
// A flatten task's height could also only be set between the lowest and the highest ground in its area (one block
// above or below on fairly flat ground): the handle, the height and the area the task works in all stopped there. They
// now reach Extra blocks further up and down, so ground can be raised or dug down to any height within that.
[Feature]
[HarmonyPatch(typeof(DraggableVolumePlane), "OnDragStarted")]
internal static class FlattenHeight
{
	// Blocks a flatten task's height may lie above its highest or below its lowest ground.
	internal const int Extra = 16;

	private static readonly Dictionary<DraggableVolumePlane, int> StartY = new Dictionary<DraggableVolumePlane, int>();

	private static void Postfix(DraggableVolumePlane __instance)
	{
		if (StartY.Count > 100)
		{
			StartY.Clear();
		}
		StartY[__instance] = __instance.GetY();
	}

	internal static bool TryStart(DraggableVolumePlane plane, out int y)
	{
		return StartY.TryGetValue(plane, out y);
	}

	// A flatten task's (or tree farm's) height plane, as opposed to a quarry's depth.
	internal static bool IsFlatten(DraggableVolumePlane plane)
	{
		return plane != null && plane.volume is RectZone;
	}
}

[Feature]
[HarmonyPatch(typeof(DraggableVolumePlane), "SetYFromDrag")]
internal static class FlattenHeightDragPatch
{
	private static bool Prefix(DraggableVolumePlane __instance)
	{
		if (!FlattenHeight.TryStart(__instance, out int start))
		{
			return true;
		}
		DragHandle handle = __instance.handle;
		int moved = (int)Brix.Util.Calcule.RoundToward(handle.Destination.y - handle.Origin.y, 0f);
		__instance.SetY(start + moved);
		return false;
	}
}

[Feature]
[HarmonyPatch(typeof(DraggableVolumePlane), nameof(DraggableVolumePlane.SetY))]
internal static class FlattenHeightClampPatch
{
	private static bool Prefix(DraggableVolumePlane __instance, int y)
	{
		if (!FlattenHeight.IsFlatten(__instance))
		{
			return true;
		}
		VoxelBounds bounds = __instance.Volume;
		int min = bounds.min.y - __instance.relativeMinY - FlattenHeight.Extra;
		int max = bounds.max.y + __instance.relativeMaxY + FlattenHeight.Extra;
		__instance.Y = Mathf.Clamp(y, min, max);
		__instance.UpdateVisuals();
		return false;
	}
}

// The handle can be dragged that far.
[Feature]
[HarmonyPatch(typeof(DraggableVolumePlane), "UpdateDrag")]
internal static class FlattenHeightHandlePatch
{
	private static void Postfix(DraggableVolumePlane __instance)
	{
		if (!FlattenHeight.IsFlatten(__instance))
		{
			return;
		}
		VoxelBounds bounds = __instance.Volume;
		Vector3 corner = __instance.CornerWithDrag;
		Vector3 min = corner.Flatten(bounds.VectorMin - Vector3.up * FlattenHeight.Extra);
		Vector3 max = corner.Flatten(bounds.VectorMax + Vector3.up * FlattenHeight.Extra);
		__instance.handle.SetMinMax(min, max);
	}
}

// The task works in its area up to the height (raised ground) or down to it (dug out), not only between its lowest
// and highest ground.
[Feature]
[HarmonyPatch(typeof(LandscapeController), "UpdateProvider")]
internal static class FlattenHeightAreaPatch
{
	private static bool Prefix(LandscapeController __instance)
	{
		LandscapeProjectUI ui = __instance.ui;
		if (ui == null || Brix.Game.User.LocalUser == null)
		{
			return true;
		}
		int threshold = ui.GetThresholdY();
		VoxelBounds bounds = ui.GetVolume();
		Vector3 min = bounds.min.ToVector3();
		Vector3 max = bounds.max.ToVector3();
		// Filled up to the height: blocks at it; dug down to it: blocks above it.
		min.y = Mathf.Min(min.y, threshold + 1);
		max.y = Mathf.Max(max.y, threshold);
		Brix.Game.User.LocalUser.GetComponent<Brix.Game.Network.UNetGroupe>().CallCmdSetLandscape(__instance.gameObject, min, max, threshold);
		return false;
	}
}
