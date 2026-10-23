using System;
using System.IO;
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
		Plugin.Root.AddComponent<RandomWorld>();
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
		string description = "A world made from a seed: " + Describe(settings) + ". " + world.Count(WorldGenerator.StartGroup) + " starting points, " + world.Count(WorldGenerator.CrystalGroup) + " crystals, " + world.Count(WorldGenerator.SpawnGroup) + " corruptron spawns.";
		MapWriter.Write(world, folder, Codename, "Random World", description, BepInEx.Paths.GameRootPath);
		File.WriteAllText(Path.Combine(folder, StampFile), Stamp(settings));
		Plugin.Log.LogInfo("RandomWorld: made " + settings + " in " + made + " ms, written in " + (watch.ElapsedMilliseconds - made) + " ms (" + world.Trees.Count + " trees, " + world.Markers.Count + " markers)");
		return Thumbnail.Render(world);
	}

	internal static string Describe(WorldSettings settings)
	{
		return settings.Style + ", " + settings.Size + ", " + settings.Biome + ", " + settings.Trees + " trees, " + settings.Boulders + " boulders, " + settings.SurfaceOre + " surface ore, " + settings.DeepOre + " deep ore, seed " + settings.Seed;
	}

	private static string Stamp(WorldSettings settings)
	{
		return "v7 " + settings;
	}

	private static bool IsWritten(WorldSettings settings)
	{
		string stamp = Path.Combine(Folder, StampFile);
		return File.Exists(stamp) && File.Exists(Path.Combine(Folder, "meta.json")) && File.ReadAllText(stamp) == Stamp(settings);
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
