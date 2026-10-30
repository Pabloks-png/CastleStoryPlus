#if !UNITY6_PLAYER
using System;
using System.Collections.Generic;
using System.Linq;
using CastleStoryPlus.Bootstrap;
using CastleStoryPlus.DevTools.Api;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Who allocates: the allocation profiler of the doorstop entry point (CastleStoryPlus.Bootstrap.AllocProfiler) counts
// every managed allocation per type, and samples the main thread's call stack every few KB. Started with the game
// (CSP_ALLOC_PROFILER=1 tools/start-game.sh) it sees everything; started here, only arrays, strings and code compiled
// since.
internal static class AllocEndpoints
{
	// Frames of these namespaces are library code: a stack's "site" is its first frame outside them.
	private static readonly string[] LibraryPrefixes = new string[7] { "System.", "Mono.", "UnityEngine.", "MoonSharp.", "Newtonsoft.", "CastleStoryPlus.Bootstrap.", "(wrapper" };

	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/allocs", "What the allocation profiler counted: bytes and objects per type (every thread), and the main thread's sampled call stacks by the bytes they stand for, summed per site (the first frame outside System, UnityEngine, MoonSharp)", Allocs)
		{
			Parameters = "top (most types, default 30), sites (most sites, default 30), stacks (most stacks, default 15), frames (frames per stack, default 10), type (only stacks of types whose name contains this text)"
		});
		routes.Add(new Route("POST", "/api/allocs/start", "Starts the allocation profiler (only arrays, strings and code compiled from now on, unless the game was started with CSP_ALLOC_PROFILER=1)", Start)
		{
			Parameters = "sampleKb (KB allocated on the main thread between two stack samples, default 32, 0 = no stacks)"
		});
		routes.Add(new Route("POST", "/api/allocs/stop", "Stops the allocation profiler (what it counted stays)", Stop));
		routes.Add(new Route("POST", "/api/allocs/reset", "Forgets what the allocation profiler counted", Reset));
	}

	private static object Start(ApiRequest request)
	{
		AllocProfiler.SampleBytes = Math.Max(0, request.Int("sampleKb", 32)) * 1024L;
		bool started = AllocProfiler.Start();
		return Obj("started", started, "state", State());
	}

	private static object Stop(ApiRequest request)
	{
		AllocProfiler.Stop();
		return State();
	}

	private static object Reset(ApiRequest request)
	{
		AllocProfiler.Reset();
		return State();
	}

	private static object Allocs(ApiRequest request)
	{
		double seconds = Math.Max(AllocProfiler.Seconds, 0.001);
		long total = Math.Max(AllocProfiler.Bytes, 1L);
		List<AllocProfiler.TypeRow> types = AllocProfiler.Types();
		List<AllocProfiler.StackRow> stacks = AllocProfiler.Stacks();
		foreach (AllocProfiler.StackRow stack in stacks)
		{
			stack.Frames = Clean(stack.Frames);
		}
		string type = request.String("type", null);
		long sampled = Math.Max(stacks.Sum((AllocProfiler.StackRow s) => s.Bytes), 1L);
		int frames = request.Int("frames", 10);
		var sites = stacks.GroupBy((AllocProfiler.StackRow s) => Site(s.Frames))
			.Select(g => new { Site = g.Key, Bytes = g.Sum((AllocProfiler.StackRow s) => s.Bytes), Samples = g.Sum((AllocProfiler.StackRow s) => s.Samples) })
			.OrderByDescending(s => s.Bytes)
			.Take(request.Int("sites", 30));
		return Obj(
			"state", State(),
			"totals", Obj(
				"objects", AllocProfiler.Objects,
				"mb", Mb(AllocProfiler.Bytes),
				"mbPerSecond", Math.Round(AllocProfiler.Bytes / seconds / 1048576.0, 2),
				"mainThreadPercent", Percent(AllocProfiler.MainThreadBytes, total),
				"sampledMb", Mb(sampled),
				"overflowMb", Mb(AllocProfiler.OverflowBytes)),
			"types", types.Take(request.Int("top", 30)).Select((AllocProfiler.TypeRow t) => Obj(
				"type", t.Name,
				"objects", t.Count,
				"mb", Mb(t.Bytes),
				"kbPerSecond", Math.Round(t.Bytes / seconds / 1024.0, 1),
				"percent", Percent(t.Bytes, total))).ToList(),
			"sites", sites.Select(s => Obj(
				"site", s.Site,
				"mb", Mb(s.Bytes),
				"percent", Percent(s.Bytes, sampled),
				"samples", s.Samples)).ToList(),
			"stacks", stacks.Where((AllocProfiler.StackRow s) => type == null || s.Type.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0).Take(request.Int("stacks", 15)).Select((AllocProfiler.StackRow s) => Obj(
				"type", s.Type,
				"mb", Mb(s.Bytes),
				"percent", Percent(s.Bytes, sampled),
				"samples", s.Samples,
				"frames", s.Frames.Take(frames).ToList())).ToList());
	}

	// Without the profiler's own frames, Mono's wrappers and a frame repeated by its wrapper.
	private static string[] Clean(string[] frames)
	{
		int start = Array.FindIndex(frames, (string f) => f.EndsWith("AllocProfiler.OnAlloc", StringComparison.Ordinal)) + 1;
		List<string> kept = new List<string>();
		for (int i = start; i < frames.Length; i++)
		{
			string frame = frames[i];
			if (frame.IndexOf("__icall_wrapper_", StringComparison.Ordinal) >= 0 || frame.IndexOf(".runtime_invoke_", StringComparison.Ordinal) >= 0 || (kept.Count > 0 && kept[kept.Count - 1] == frame))
			{
				continue;
			}
			kept.Add(frame);
		}
		return kept.ToArray();
	}

	private static string Site(string[] frames)
	{
		foreach (string frame in frames)
		{
			if (!LibraryPrefixes.Any((string prefix) => frame.StartsWith(prefix, StringComparison.Ordinal)))
			{
				return frame;
			}
		}
		return (frames.Length > 0) ? frames[frames.Length - 1] : "?";
	}

	private static object State()
	{
		return Obj(
			"installed", AllocProfiler.Installed,
			"running", AllocProfiler.Running,
			"fromStart", AllocProfiler.FromStart,
			"error", AllocProfiler.Error,
			"seconds", Math.Round(AllocProfiler.Seconds, 1),
			"sampleKb", AllocProfiler.SampleBytes / 1024);
	}

	private static double Percent(long part, long whole)
	{
		return Math.Round(100.0 * part / whole, 1);
	}

	private static double Mb(long bytes)
	{
		return Math.Round(bytes / 1048576.0, 2);
	}
}
#endif
