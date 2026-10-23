using Brix.Input;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CastleStoryPlus.LuaUi;

// The wheel over a menu scrolls the menu, not the camera: the game's camera reads the wheel without asking whether
// the mouse is over UI, so what it read this frame is undone then (as MinimapZoom does for the minimap).
[Feature(Features.CsMenus, Features.CsMenusInfo)]
[HarmonyPatch(typeof(CameraScript), nameof(CameraScript.GetMovementVectorsFromInputs))]
internal static class MenuWheelGuard
{
	private static void Prefix(CameraScript __instance, out Vector2 __state)
	{
		__state = new Vector2(__instance._zoomInput, __instance._climbSpeed);
	}

	private static void Postfix(CameraScript __instance, Vector2 __state)
	{
		if (Input.mouseScrollDelta.y != 0f && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
		{
			__instance._zoomInput = __state.x;
			__instance._climbSpeed = __state.y;
		}
	}
}
