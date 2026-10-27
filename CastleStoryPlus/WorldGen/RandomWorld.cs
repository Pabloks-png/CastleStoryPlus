using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using Brix.Assets;
using CastleStoryPlus.Core;
using UnityEngine;

namespace CastleStoryPlus.WorldGen;

// A "Random World" map in the new game list, made from a seed with noise (WorldGenerator). Its style, size, biome
// and seed are chosen on the new game screen (RandomWorldMenu); each change makes the world again in the background,
// writes it as an ordinary map folder in Info/UGC/Maps and shows it as the map's picture. Starting the map waits for
// the newest world. The settings are kept in the config, so the same world comes back after a restart.
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
internal class RandomWorld : MonoBehaviour
{
	internal const string Codename = "csp_random_world";

	private const string FolderName = "CastleStoryPlus Random World";

	// The settings the files on disk were made with.
	private const string StampFile = "csp_world.txt";

	// The starting points, one "x y z" line each, in the order the menu numbers them.
	private const string StartsFile = "csp_starts.txt";

	private static ConfigEntry<int> _start;

	private static ConfigEntry<string> _seed;

	private static ConfigEntry<WorldStyle> _style;

	private static ConfigEntry<WorldSize> _size;

	private static ConfigEntry<WorldBiome> _biome;

	private static ConfigEntry<WorldAmount> _trees;

	private static ConfigEntry<WorldAmount> _boulders;

	private static ConfigEntry<WorldAmount> _surfaceOre;

	private static ConfigEntry<WorldAmount> _deepOre;

	private static readonly object Gate = new object();

	private static Thread _job;

	// Settings waiting for the running job to finish, or null.
	private static WorldSettings? _queued;

	// Set by the job, picked up on the main thread.
	private static byte[] _finishedPicture;

	private static string _failure;

	private static Texture2D _preview;

	// The map entries of the random world the menus have shown (re-read once a new world is written).
	private static Asset_Map _shownMap;

	internal static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath, "Info/UGC/Maps"), FolderName);

	internal static bool Busy
	{
		get
		{
			lock (Gate)
			{
				return _job != null;
			}
		}
	}

	internal static WorldSettings Settings => new WorldSettings
	{
		Seed = _seed.Value,
		Style = _style.Value,
		Size = _size.Value,
		Biome = _biome.Value,
		Trees = _trees.Value,
		Boulders = _boulders.Value,
		SurfaceOre = _surfaceOre.Value,
		DeepOre = _deepOre.Value
	};

	private static void Enable()
	{
		_seed = Plugin.Cfg.Bind("RandomWorld", "Seed", string.Empty, "Seed of the random world: a UUID (or any text); the same seed and settings always make the same world. Empty: a new UUID.");
		if (_seed.Value.Trim().Length == 0)
		{
			_seed.Value = NewSeed();
		}
		_style = Plugin.Cfg.Bind("RandomWorld", "Style", WorldStyle.Archipelago, "Archipelago (many floating islands), Continent (one big island) or Highlands (one island with mountains).");
		_size = Plugin.Cfg.Bind("RandomWorld", "Size", WorldSize.Medium, "Small, Medium or Large.");
		_biome = Plugin.Cfg.Bind("RandomWorld", "Biome", WorldBiome.Green, "Green, Winter or Desert.");
		_trees = Plugin.Cfg.Bind("RandomWorld", "Trees", WorldAmount.Normal, "How many trees: None, Few, Normal, Many or Lots.");
		_boulders = Plugin.Cfg.Bind("RandomWorld", "Boulders", WorldAmount.Normal, "How many boulders (stone on the surface): None, Few, Normal, Many or Lots.");
		_surfaceOre = Plugin.Cfg.Bind("RandomWorld", "SurfaceOre", WorldAmount.Normal, "How much iron and pyrite on the surface: None, Few, Normal, Many or Lots.");
		_deepOre = Plugin.Cfg.Bind("RandomWorld", "DeepOre", WorldAmount.Normal, "How much iron, crystal and coal in the deep rock: None, Few, Normal, Many or Lots.");
		_start = Plugin.Cfg.Bind("RandomWorld", "Start", 0, "Single player: the starting point to begin at, as numbered on the map's picture (0: a random one, as the gamemode picks).");
		Plugin.Root.AddComponent<RandomWorld>();
		Plugin.Root.AddComponent<RandomWorldPanel>();
		// The map list is read at the title screen, so the folder has to be there before it.
		try
		{
			if (!IsWritten(Settings))
			{
				Write(Settings);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("RandomWorld: could not make the world: " + ex);
		}
	}

	// The chosen starting point (1 is the first), or 0 for a random one.
	internal static int ChosenStart
	{
		get
		{
			return Math.Max(0, _start.Value);
		}
		set
		{
			_start.Value = Math.Max(0, value);
		}
	}

	// The starting points of the world on disk, in their numbered order.
	internal static List<Vector3> Starts()
	{
		if (_starts == null)
		{
			_starts = ReadStarts();
		}
		return _starts;
	}

	// A line for the panel: how many starting points the world has.
	internal static string Summary()
	{
		int count = Starts().Count;
		return count + " starting points (numbered on the picture), " + Settings.Size.ToString().ToLowerInvariant() + " " + Settings.Style.ToString().ToLowerInvariant() + ".";
	}

	// Read again when a new world is written.
	private static List<Vector3> _starts;

	private static List<Vector3> ReadStarts()
	{
		List<Vector3> starts = new List<Vector3>();
		try
		{
			string file = Path.Combine(Folder, StartsFile);
			if (!File.Exists(file))
			{
				return starts;
			}
			foreach (string line in File.ReadAllLines(file))
			{
				string[] parts = line.Split(' ');
				if (parts.Length == 3)
				{
					starts.Add(new Vector3(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture)));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("RandomWorld: could not read the starting points: " + ex.Message);
		}
		return starts;
	}

	internal static bool IsRandomWorld(Asset_Map map)
	{
		// A save of it keeps the codename, but is played from its own folder.
		return map != null && map.codename == Codename && map.IsNewGame();
	}

	// The world's picture, once one was made this session (the file on disk is read otherwise).
	internal static Texture2D Preview => _preview;

	internal static void Shown(Asset_Map map)
	{
		_shownMap = map;
	}

	internal static void Change(WorldSettings settings)
	{
		_seed.Value = settings.Seed;
		_style.Value = settings.Style;
		_size.Value = settings.Size;
		_biome.Value = settings.Biome;
		_trees.Value = settings.Trees;
		_boulders.Value = settings.Boulders;
		_surfaceOre.Value = settings.SurfaceOre;
		_deepOre.Value = settings.DeepOre;
		lock (Gate)
		{
			if (_job != null)
			{
				_queued = settings;
				return;
			}
			Start(settings);
		}
	}

	internal static string NewSeed()
	{
		return Guid.NewGuid().ToString();
	}

	// Called before the map is loaded: waits for the job and makes sure the files match the chosen settings.
	internal static void EnsureWritten(Asset_Map map)
	{
		if (!IsRandomWorld(map))
		{
			return;
		}
		Thread job;
		lock (Gate)
		{
			job = _job;
		}
		job?.Join();
		lock (Gate)
		{
			job = _job;
		}
		job?.Join();
		WorldSettings settings = Settings;
		if (!IsWritten(settings))
		{
			Write(settings);
		}
		map.Disk_ReadMeta();
		Plugin.Log.LogInfo("RandomWorld: starting " + settings);
	}

	private static void Start(WorldSettings settings)
	{
		_job = new Thread(() => Run(settings)) { IsBackground = true, Name = "CastleStoryPlus world generator" };
		_job.Start();
	}

	private static void Run(WorldSettings settings)
	{
		byte[] picture = null;
		string failure = null;
		try
		{
			picture = Write(settings);
		}
		catch (Exception ex)
		{
			failure = ex.ToString();
		}
		lock (Gate)
		{
			_finishedPicture = picture;
			_failure = failure;
			_job = null;
			if (_queued.HasValue)
			{
				WorldSettings next = _queued.Value;
				_queued = null;
				Start(next);
			}
		}
	}

	// Makes and writes the world; returns its picture (rows from the top).
	private static byte[] Write(WorldSettings settings)
	{
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
		GeneratedWorld world = WorldGenerator.Generate(settings);
		long made = watch.ElapsedMilliseconds;
		string folder = Folder;
		// Short enough for the map's card; the share code holds every setting (a save of the world is read by it).
		string description = settings.Size + " " + settings.Style.ToString().ToLowerInvariant() + ", " + settings.Biome.ToString().ToLowerInvariant() + ". " + world.Count(WorldGenerator.StartGroup) + " starts, " + world.Count(WorldGenerator.CrystalGroup) + " crystals. Share code: " + ShareCode(settings);
		MapWriter.Write(world, folder, Codename, "Random World", description, BepInEx.Paths.GameRootPath);
		StringBuilder starts = new StringBuilder();
		foreach (GeneratedWorld.Marker marker in world.Markers)
		{
			if (marker.Group == WorldGenerator.StartGroup)
			{
				starts.Append(marker.X.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(marker.Y.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(marker.Z.ToString(CultureInfo.InvariantCulture)).Append('\n');
			}
		}
		File.WriteAllText(Path.Combine(folder, StartsFile), starts.ToString());
		File.WriteAllText(Path.Combine(folder, StampFile), Stamp(settings));
		Plugin.Log.LogInfo("RandomWorld: made " + settings + " in " + made + " ms, written in " + (watch.ElapsedMilliseconds - made) + " ms (" + world.Trees.Count + " trees, " + world.Markers.Count + " markers)");
		return Thumbnail.Render(world);
	}

	private const string SharePrefix = "csp1:";

	// A world's share code: every setting and the seed, so another player gets the same world from it
	// ("csp1:Archipelago,Large,Green,Normal,Normal,Many,Normal:<seed>").
	internal static string ShareCode(WorldSettings settings)
	{
		return SharePrefix + settings.Style + "," + settings.Size + "," + settings.Biome + "," + settings.Trees + "," + settings.Boulders + "," + settings.SurfaceOre + "," + settings.DeepOre + ":" + settings.Seed;
	}

	// A share code read back (anywhere in the text, e.g. a save's description); false when there is none.
	internal static bool TryParseShareCode(string text, out WorldSettings settings)
	{
		settings = Settings;
		int at = (text != null) ? text.IndexOf(SharePrefix, StringComparison.OrdinalIgnoreCase) : -1;
		if (at < 0)
		{
			return false;
		}
		string rest = text.Substring(at + SharePrefix.Length);
		int colon = rest.IndexOf(':');
		if (colon < 0)
		{
			return false;
		}
		string[] parts = rest.Substring(0, colon).Split(',');
		string seed = rest.Substring(colon + 1).Trim();
		int end = seed.IndexOfAny(new[] { ' ', '\n', '\r', '\t' });
		if (end >= 0)
		{
			seed = seed.Substring(0, end);
		}
		seed = seed.TrimEnd('.', ',', ';').ToLowerInvariant();
		if (parts.Length != 7 || seed.Length == 0)
		{
			return false;
		}
		try
		{
			settings.Style = (WorldStyle)Enum.Parse(typeof(WorldStyle), parts[0].Trim(), true);
			settings.Size = (WorldSize)Enum.Parse(typeof(WorldSize), parts[1].Trim(), true);
			settings.Biome = (WorldBiome)Enum.Parse(typeof(WorldBiome), parts[2].Trim(), true);
			settings.Trees = (WorldAmount)Enum.Parse(typeof(WorldAmount), parts[3].Trim(), true);
			settings.Boulders = (WorldAmount)Enum.Parse(typeof(WorldAmount), parts[4].Trim(), true);
			settings.SurfaceOre = (WorldAmount)Enum.Parse(typeof(WorldAmount), parts[5].Trim(), true);
			settings.DeepOre = (WorldAmount)Enum.Parse(typeof(WorldAmount), parts[6].Trim(), true);
		}
		catch (ArgumentException)
		{
			return false;
		}
		settings.Seed = seed;
		return true;
	}

	// The world a random world's save was made from: its share code, or for saves made before share codes, the
	// settings and seed written out in its description.
	internal static bool TryParseDescription(string description, out WorldSettings settings)
	{
		if (TryParseShareCode(description, out settings))
		{
			return true;
		}
		System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(description ?? string.Empty, @"(\w+), (\w+), (\w+), (\w+) trees, (\w+) boulders, (\w+) surface ore, (\w+) deep ore, seed ([^\s]+?)\.(\s|$)");
		if (!match.Success)
		{
			return false;
		}
		string code = SharePrefix + match.Groups[1].Value + "," + match.Groups[2].Value + "," + match.Groups[3].Value + "," + match.Groups[4].Value + "," + match.Groups[5].Value + "," + match.Groups[6].Value + "," + match.Groups[7].Value + ":" + match.Groups[8].Value;
		return TryParseShareCode(code, out settings);
	}

	internal static string Describe(WorldSettings settings)
	{
		return settings.Style + ", " + settings.Size + ", " + settings.Biome + ", " + settings.Trees + " trees, " + settings.Boulders + " boulders, " + settings.SurfaceOre + " surface ore, " + settings.DeepOre + " deep ore, seed " + settings.Seed;
	}

	private static string Stamp(WorldSettings settings)
	{
		return "v16 " + settings;
	}

	private static bool IsWritten(WorldSettings settings)
	{
		string stamp = Path.Combine(Folder, StampFile);
		return File.Exists(stamp) && File.Exists(Path.Combine(Folder, "meta.json")) && File.Exists(Path.Combine(Folder, StartsFile)) && File.ReadAllText(stamp) == Stamp(settings);
	}

	private void Update()
	{
		byte[] picture;
		string failure;
		lock (Gate)
		{
			picture = _finishedPicture;
			failure = _failure;
			_finishedPicture = null;
			_failure = null;
		}
		if (failure != null)
		{
			Plugin.Log.LogError("RandomWorld: could not make the world: " + failure);
		}
		if (picture == null)
		{
			return;
		}
		_starts = null;
		ShowPicture(picture);
		if (_shownMap != null)
		{
			_shownMap.Disk_ReadMeta();
			_shownMap.thumbnail = _preview;
		}
		RandomWorldMenu.RefreshOpen();
	}

	private static void ShowPicture(byte[] rgba)
	{
		const int W = Thumbnail.Width;
		const int H = Thumbnail.Height;
		if (_preview == null)
		{
			_preview = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "RandomWorldPreview", wrapMode = TextureWrapMode.Clamp };
		}
		Color32[] pixels = new Color32[W * H];
		// Unity textures start at the bottom row.
		for (int y = 0; y < H; y++)
		{
			int from = (H - 1 - y) * W * 4;
			for (int x = 0; x < W; x++)
			{
				int o = from + x * 4;
				pixels[x + y * W] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
			}
		}
		_preview.SetPixels32(pixels);
		_preview.Apply(false);
	}
}

// How much ore the deep rock of a new random world holds: the game picks iron, crystal (and the mod's coal) where
// their noise is above a threshold, so the thresholds are raised for less and lowered for more. A save keeps them in
// its noise.json (except coal, which goes back to normal).
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyLib.HarmonyPatch(typeof(Brix.Engine.VoxelNoiseConfigurator), nameof(Brix.Engine.VoxelNoiseConfigurator.Init))]
internal static class RandomWorldDeepOrePatch
{
	private static Brix.Engine.VoxelNoiseConfigurator _done;

	private static void Postfix()
	{
		Brix.Engine.VoxelNoiseConfigurator configurator = Brix.Engine.VoxelNoiseConfigurator.Instance;
		if (configurator == null || configurator == _done || !RandomWorld.IsRandomWorld(GameParam.Map))
		{
			return;
		}
		_done = configurator;
		WorldAmount amount = RandomWorld.Settings.DeepOre;
		if (amount == WorldAmount.Normal)
		{
			return;
		}
		foreach (Brix.Engine.VoxelNoiseConfigurator.NoiseConfig config in configurator.NoiseConfigList)
		{
			if (config.Type == (int)Brix.Engine.VoxelTerrainType.Stone)
			{
				continue;
			}
			float room = 1f - config.Threshold;
			switch (amount)
			{
				case WorldAmount.None:
					config.Threshold = 100f;
					break;
				case WorldAmount.Few:
					config.Threshold += room * 0.25f;
					break;
				case WorldAmount.Many:
					config.Threshold -= room * 0.15f;
					break;
				case WorldAmount.Lots:
					config.Threshold -= room * 0.3f;
					break;
			}
		}
		Plugin.Log.LogInfo("RandomWorld: deep ore " + amount);
	}
}
