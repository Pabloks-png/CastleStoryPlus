using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.DevTools.Frames;
using CastleStoryPlus.DevTools.Worlds;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The benchmark: building its save (Worlds/BenchmarkWorld) and measuring a run on it (Frames/BenchmarkRun).
internal static class BenchmarkEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/benchmark/build", "Builds the benchmark save \"" + BenchmarkWorld.SaveName + "\" with only the original game's things: a new sandbox world on Big Island (the running game is left without saving), a 120 x 80 platform near the home crystal levelled like the world editor's plateau brush and walled round (towers, doors, lantern wards), every workshop and block of the build menu, 48 filled stockpiles, 70 bricktrons, loose resources, workshops on a loop, a harvest task in the forest, a quarry and a keep to build; it runs 30 s and is saved. Answers at once; GET /api/benchmark follows it", Build)
		{
			Parameters = "enemies (corruptrons raiding the base, default 0), newWorld (false: build on the running game instead)"
		});
		routes.Add(new Route("POST", "/api/benchmark/run", "Measures the loaded game (meant: the \"" + BenchmarkWorld.SaveName + "\" save, CSP_AUTOLOAD) at normal speed: a warm-up, then four fixed camera views on the platform, each for the same time: fps, 1% low, frame time percentiles, garbage collections and Unity's player loop steps. Writes BepInEx/devtools/benchmarks/benchmark-<date>.json. Answers at once; GET /api/benchmark gives the report. CSP_BENCHMARK=1 starts a run by itself once a game is loaded, CSP_BENCHMARK=quit also closes the game after", Run)
		{
			Parameters = "warmup (seconds, default 20), seconds (per view, default 15)"
		});
		routes.Add(new Route("GET", "/api/benchmark", "The benchmark: building its save (state, log, pieces placed and refused) and the last run (state, report, file)", Get));
	}

	private static object Build(ApiRequest request)
	{
		BenchmarkWorld.Instance.Begin(request.Int("enemies", 0), request.Bool("newWorld", true));
		return Get(request);
	}

	private static object Run(ApiRequest request)
	{
		BenchmarkRun.Instance.Begin(request.Float("warmup", 20f), request.Float("seconds", 15f));
		return Get(request);
	}

	private static object Get(ApiRequest request)
	{
		return Obj(
			"build", BenchmarkWorld.Instance.Status(),
			"pieces", BenchmarkWorld.Instance.Placed(),
			"run", BenchmarkRun.Instance.Status());
	}
}
