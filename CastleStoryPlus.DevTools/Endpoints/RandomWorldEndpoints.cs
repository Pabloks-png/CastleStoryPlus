using System;
using System.Collections.Generic;
using System.IO;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.WorldGen;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The random world generator: makes a world from settings and saves its picture (as on the new game screen) without
// touching the Random World map, to look at many seeds and settings quickly.
internal static class RandomWorldEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/randomworld/preview", "Makes a random world and saves its picture as BepInEx/devtools/worlds/<seed>.png (the Random World map is not changed); answers with the path, timings and counts", Preview)
		{
			Parameters = "seed (default: a new UUID), style (Archipelago, Continent, Highlands), size (Small, Medium, Large), biome (Green, Winter, Desert), trees, boulders, surfaceOre, deepOre (None, Few, Normal, Many, Lots); unset ones are the Random World map's",
			TimeoutMs = 60000
		});
	}

	private static object Preview(ApiRequest request)
	{
		WorldSettings settings = RandomWorld.Settings;
		settings.Seed = request.String("seed", Guid.NewGuid().ToString());
		settings.Style = Parse(request, "style", settings.Style);
		settings.Size = Parse(request, "size", settings.Size);
		settings.Biome = Parse(request, "biome", settings.Biome);
		settings.Trees = Parse(request, "trees", settings.Trees);
		settings.Boulders = Parse(request, "boulders", settings.Boulders);
		settings.SurfaceOre = Parse(request, "surfaceOre", settings.SurfaceOre);
		settings.DeepOre = Parse(request, "deepOre", settings.DeepOre);
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
		GeneratedWorld world = WorldGenerator.Generate(settings);
		long made = watch.ElapsedMilliseconds;
		string folder = Path.Combine(Path.Combine(BepInEx.Paths.BepInExRootPath, "devtools"), "worlds");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, settings.Style + "-" + settings.Size + "-" + settings.Biome + "-" + settings.Seed + ".png");
		File.WriteAllBytes(path, Thumbnail.Encode(world));
		return Obj(
			"path", path,
			"settings", settings.ToString(),
			"generateMs", made,
			"trees", world.Trees.Count,
			"plants", world.Plants.Count,
			"boulders", world.Boulders.Count,
			"iron", world.Iron.Count,
			"pyrite", world.Pyrite.Count,
			"starts", world.Count(WorldGenerator.StartGroup),
			"crystals", world.Count(WorldGenerator.CrystalGroup),
			"spawns", world.Count(WorldGenerator.SpawnGroup));
	}

	private static T Parse<T>(ApiRequest request, string name, T fallback)
	{
		string value = request.String(name, string.Empty);
		return (value.Length == 0) ? fallback : (T)Enum.Parse(typeof(T), value, true);
	}
}
