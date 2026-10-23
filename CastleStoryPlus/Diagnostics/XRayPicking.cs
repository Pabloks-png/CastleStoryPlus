using Brix.Input;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Diagnostics;

// With the X-ray on, the mouse points into the tunnels it shows: a ray that hits the ground goes on through it to the
// first space dug out under the ground and hits what is in there (the floor or far wall of the tunnel, a bricktron),
// so things are placed and picked in the tunnel instead of on the surface above it. A ray that meets no dug out space
// keeps its hit on the surface.
[Feature(Features.DebugMenu, Features.DebugMenuInfo)]
[HarmonyPatch(typeof(VoxelRaycastHit), nameof(VoxelRaycastHit.PlaceAtRaycast))]
internal static class XRayPicking
{
	private static VoxelRaycastHit _saved;

	private static void Postfix(VoxelRaycastHit __instance, Ray ray, float distance, LayerBundle layers, bool useDefaultPlane, bool isRetry, ref bool __result)
	{
		if (isRetry || useDefaultPlane || XRayView.Current == XRayView.Mode.Off || !__instance.HasHit || !__instance.HasHitTerrain())
		{
			return;
		}
		if (!XRayView.FindHollow(__instance.WorldHitVector, ray.direction, out Vector3 inside))
		{
			return;
		}
		_saved = _saved ?? new VoxelRaycastHit();
		_saved.Copy(__instance);
		bool result = __instance.PlaceAtRaycast(new Ray(inside, ray.direction), distance, layers, false, true);
		if (!__instance.HasHit)
		{
			__instance.Copy(_saved);
			return;
		}
		__result = result;
	}
}
