using Brix.Input;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Core;

// Coming back from alt+tab, the game (Rewired) sees the Tab of alt+tab as a fresh press of the build-mode key
// (cam_buildmode, Tab) once Alt is already up, and the construction menu opens. The key is ignored for GraceSeconds
// after the game window regains focus. OnApplicationFocus runs before that frame's Update.
[Feature(Features.FocusInputGuard, Features.FocusInputGuardInfo)]
internal class FocusInputGuard : MonoBehaviour
{
	private const float GraceSeconds = 0.5f;

	private static float _focusRegainedAt = -10f;

	internal static bool Suppressed => Time.unscaledTime - _focusRegainedAt < GraceSeconds;

	private static void Enable()
	{
		Plugin.Root.AddComponent<FocusInputGuard>();
	}

	private void OnApplicationFocus(bool focus)
	{
		if (focus)
		{
			_focusRegainedAt = Time.unscaledTime;
		}
	}
}

[Feature(Features.FocusInputGuard, Features.FocusInputGuardInfo)]
[HarmonyPatch(typeof(InputModeController), "BuildModeButtonDown", MethodType.Getter)]
internal static class FocusInputGuardBuildModePatch
{
	private static void Postfix(ref bool __result)
	{
		if (__result && FocusInputGuard.Suppressed)
		{
			__result = false;
		}
	}
}
