#if UNITY6
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.LowLevel;

namespace CastleStoryPlus.DevTools.Frames;

// Times each of Unity's own player loop steps (animation, canvases, renderers, physics, scripts, rendering and so on)
// by putting a time stamp after every step: a step's time is the time since the stamp before it. This sees what the
// frame monitor and the Profiler-less phases cannot split up, and works in a release player. Started by the first call
// of /api/frames/loop; costs one Stopwatch read per step (about 150 a frame). The managed heap's growth since the stamp
// before (GC.GetTotalMemory) is counted the same way: roughly what each step allocates. Other threads' allocations land
// in whichever step runs at the time, and the time between frames (where Unity runs its incremental collection) shows
// heap growth that is not the main thread's; GC.GetAllocatedBytesForCurrentThread would be exact, but Unity 6's Mono
// answers 0.
internal static class PlayerLoopTiming
{
	internal sealed class Step
	{
		internal string Name;

		internal double Ms;

		internal double MaxMs;

		internal long Calls;

		internal double FrameMs;

		internal long Bytes;
	}

	internal static bool Running { get; private set; }

	internal static readonly List<Step> Steps = new List<Step>();

	internal static long Frames;

	internal static readonly Stopwatch Since = new Stopwatch();

	private static readonly Stopwatch Clock = Stopwatch.StartNew();

	private static double _last;

	private static long _lastBytes;

	private static PlayerLoopSystem _original;

	private static Step _between;

	internal static void Start()
	{
		if (Running)
		{
			return;
		}
		_original = PlayerLoop.GetCurrentPlayerLoop();
		PlayerLoopSystem loop = _original;
		Steps.Clear();
		_between = Add("(between frames: present, frame cap, input)");
		PlayerLoopSystem[] phases = loop.subSystemList;
		PlayerLoopSystem[] timed = new PlayerLoopSystem[phases.Length];
		for (int p = 0; p < phases.Length; p++)
		{
			PlayerLoopSystem phase = phases[p];
			string phaseName = (phase.type != null) ? phase.type.Name : "?";
			List<PlayerLoopSystem> list = new List<PlayerLoopSystem>();
			list.Add(Marker(Add(phaseName + " (start)"), p == 0));
			if (phase.subSystemList != null)
			{
				foreach (PlayerLoopSystem step in phase.subSystemList)
				{
					list.Add(step);
					string stepName = (step.type != null) ? step.type.Name : "?";
					list.Add(Marker(Add(phaseName + "." + stepName), false));
				}
			}
			phase.subSystemList = list.ToArray();
			timed[p] = phase;
		}
		loop.subSystemList = timed;
		Reset();
		PlayerLoop.SetPlayerLoop(loop);
		Running = true;
	}

	internal static void Stop()
	{
		if (!Running)
		{
			return;
		}
		PlayerLoop.SetPlayerLoop(_original);
		Running = false;
	}

	internal static void Reset()
	{
		foreach (Step step in Steps)
		{
			step.Ms = 0;
			step.MaxMs = 0;
			step.Calls = 0;
			step.FrameMs = 0;
			step.Bytes = 0;
		}
		Frames = 0;
		_last = Clock.Elapsed.TotalMilliseconds;
		Since.Restart();
	}

	private static Step Add(string name)
	{
		Step step = new Step { Name = name };
		Steps.Add(step);
		return step;
	}

	// The first stamp of a frame also closes the frame before it: the time since the last stamp is spent outside the
	// loop, and each step's time in the finished frame counts towards its worst frame.
	private static PlayerLoopSystem Marker(Step step, bool frameStart)
	{
		PlayerLoopSystem marker = new PlayerLoopSystem { type = typeof(PlayerLoopTiming) };
		marker.updateDelegate = delegate
		{
			double now = Clock.Elapsed.TotalMilliseconds;
			double ms = now - _last;
			_last = now;
			long bytes = System.GC.GetTotalMemory(false);
			long grown = bytes - _lastBytes;
			_lastBytes = bytes;
			if (frameStart)
			{
				foreach (Step each in Steps)
				{
					if (each.FrameMs > each.MaxMs)
					{
						each.MaxMs = each.FrameMs;
					}
					each.FrameMs = 0;
				}
				Count(_between, ms, grown);
				Frames++;
			}
			Count(step, frameStart ? 0 : ms, frameStart ? 0 : grown);
		};
		return marker;
	}

	private static void Count(Step step, double ms, long grown)
	{
		if (grown > 0)
		{
			step.Bytes += grown;
		}
		step.Ms += ms;
		step.FrameMs += ms;
		step.Calls++;
	}
}
#endif
