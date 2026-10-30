#if UNITY6
using System;
using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.DevTools.Frames;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Unity's own player loop steps, timed (Frames/PlayerLoopTiming): what the "unmeasured" time is made of.
internal static class PlayerLoopEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/frames/loop", "Each of Unity's player loop steps (animation, canvases, renderers, physics, scripts, rendering, the time between frames) timed since the last reset: ms per frame, worst frame, runs per frame and KB allocated per frame; the first call starts the timing", Get)
		{
			Parameters = "top (most steps, default 40), sort (ms or kb: by time, the default, or by what each step allocates), reset (true: start again from zero), stop (true: take the time stamps out again)"
		});
	}

	private static object Get(ApiRequest request)
	{
		if (request.Bool("stop", false))
		{
			PlayerLoopTiming.Stop();
			return Obj("running", false);
		}
		bool started = !PlayerLoopTiming.Running;
		PlayerLoopTiming.Start();
		long frames = PlayerLoopTiming.Frames;
		double seconds = PlayerLoopTiming.Since.Elapsed.TotalSeconds;
		List<PlayerLoopTiming.Step> steps = new List<PlayerLoopTiming.Step>(PlayerLoopTiming.Steps);
		if (request.String("sort", "ms") == "kb")
		{
			steps.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
		}
		else
		{
			steps.Sort((a, b) => b.Ms.CompareTo(a.Ms));
		}
		int top = request.Int("top", 40);
		List<object> rows = new List<object>();
		double total = 0;
		foreach (PlayerLoopTiming.Step step in steps)
		{
			total += step.Ms;
		}
		foreach (PlayerLoopTiming.Step step in steps)
		{
			if (rows.Count >= top || step.Calls == 0)
			{
				break;
			}
			rows.Add(Obj(
				"step", step.Name,
				"msPerFrame", (frames > 0) ? (float)Math.Round(step.Ms / frames, 3) : 0f,
				"maxMsInFrame", Round((float)step.MaxMs),
				"runsPerFrame", (frames > 0) ? Round((float)step.Calls / frames) : 0f,
				"kbPerFrame", (frames > 0) ? Round((float)(step.Bytes / 1024.0 / frames)) : 0f));
		}
		if (request.Bool("reset", false))
		{
			PlayerLoopTiming.Reset();
		}
		return Obj(
			"startedNow", started,
			"frames", frames,
			"fps", (seconds > 0) ? Round((float)(frames / seconds)) : 0f,
			"frameMs", (frames > 0) ? Round((float)(total / frames)) : 0f,
			"steps", rows);
	}
}
#endif
