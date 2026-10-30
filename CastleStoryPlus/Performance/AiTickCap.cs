using BepInEx.Configuration;
using Brix.Game;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// The game runs its AI (every unit's and project's behaviour tree, a unit's enemy watch, its animation tracking) in
// the fixed update, 50 times a game second. When a frame takes longer than 20 ms Unity runs several fixed updates in
// it to catch up: at 10 frames a second five, so the AI did five times the work in every frame, which made the frame
// slower still. The AI now runs in at most AiTicksPerFrame of a frame's fixed updates (times the game speed when it
// is sped up); physics and the rest of the fixed update run in all of them. Above 50 / AiTicksPerFrame frames a
// second nothing changes. Below that the units decide a little later in game time (their waits are counted in AI
// ticks), they walk and work at the same speed. The animators move on once per frame, so the skipped ticks would
// have seen the same animation states.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch]
internal static class AiTickCap
{
	private const int DefaultTicksPerFrame = 2;

	internal static ConfigEntry<int> TicksPerFrame;

	private static int _frame = -1;

	private static int _step;

	// Whether this fixed update runs the AI; worked out once per fixed update.
	private static bool _run = true;

	private static void Enable()
	{
		TicksPerFrame = Plugin.Cfg.Bind("Performance", "AiTicksPerFrame", DefaultTicksPerFrame, new ConfigDescription("Most fixed updates in one frame in which the units' AI runs (0: all of them, as the original game). When the frame rate drops below 50 a second Unity runs several fixed updates a frame to catch up; the AI running in all of them made the frame slower still.", new AcceptableValueRange<int>(0, 10)));
	}

	private static void Disable()
	{
		_run = true;
	}

	[HarmonyPatch(typeof(GameClock), "FixedUpdate")]
	[HarmonyPrefix]
	private static void CountStep()
	{
		if (Time.frameCount != _frame)
		{
			_frame = Time.frameCount;
			_step = 0;
		}
		_step++;
		int cap = TicksPerFrame != null ? TicksPerFrame.Value : DefaultTicksPerFrame;
		_run = cap <= 0 || _step <= cap * Mathf.Max(1, Mathf.RoundToInt(Time.timeScale));
	}

	// AnimatorTracker's update is replaced by AnimatorTrackerCache, which asks this first.
	internal static bool Run
	{
		get { return _run; }
	}

	[HarmonyPatch(typeof(Behavior), "BrixFixedUpdate")]
	[HarmonyPrefix]
	private static bool Behaviour()
	{
		return _run;
	}
}
