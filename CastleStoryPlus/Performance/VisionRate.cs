using Brix.Game;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Who sees whom (fog of war, units spotting enemies) is refreshed in small steps: each faction's SharedFactionVision
// moves one shared pass on by a step every frame (marking, a few tracking groups at a time, removing, adding). The
// work per second grew with the frame rate: at 200 fps three times what it was at 60, with nothing gained. The steps
// now go at most at the rate of 60 frames a second (each faction's step comes when its share of time is due); below
// 60 fps nothing changes.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(SharedFactionVision), "BrixUpdate")]
internal static class VisionRate
{
	private const float StepsPerSecond = 60f;

	private static int _frame = -1;

	// Calls this frame and the frame before (one per faction).
	private static int _calls;

	private static int _callsBefore;

	private static float _credit;

	private static bool Prefix()
	{
		if (Time.frameCount != _frame)
		{
			_frame = Time.frameCount;
			_callsBefore = Mathf.Max(_calls, 1);
			_calls = 0;
			_credit = Mathf.Min(_credit + Time.unscaledDeltaTime * StepsPerSecond * _callsBefore, _callsBefore);
		}
		_calls++;
		if (_credit < 1f)
		{
			return false;
		}
		_credit -= 1f;
		return true;
	}
}
