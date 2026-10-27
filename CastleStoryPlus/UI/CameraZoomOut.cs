using BepInEx.Configuration;
using Brix.Input;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.UI;

// The camera zooms further out: its zoom out limit (ZoomOutVector.Limit, set in the camera's prefab) is raised to
// [Camera] MaxZoomOut each frame. The world editor's own camera (limit 300) and a lower limit set by the game are
// left alone; the limit is only raised, never lowered.
[Feature(Features.CameraZoomOut, Features.CameraZoomOutInfo)]
[HarmonyPatch(typeof(CameraScript), nameof(CameraScript.Update))]
internal static class CameraZoomOut
{
	private static ConfigEntry<float> _maxZoomOut;

	private static void Enable()
	{
		_maxZoomOut = Plugin.Cfg.Bind("Camera", "MaxZoomOut", 250f, "How far out the camera can zoom, in blocks from the point it looks at (the game's own limit is used when it is higher).");
	}

	private static void Prefix(CameraScript __instance)
	{
		if (_maxZoomOut == null || __instance.editorCamera || __instance.worldEditorCamera)
		{
			return;
		}
		float limit = Mathf.Max(_maxZoomOut.Value, 1f);
		if (__instance.ZoomOutVector.Limit < limit)
		{
			__instance.ZoomOutVector.Limit = limit;
		}
	}
}
