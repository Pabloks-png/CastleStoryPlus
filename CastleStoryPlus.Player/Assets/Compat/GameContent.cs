using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CastleStoryPlus.Compat
{
	// The game's data, converted from the player's own install into asset bundles (CastleStoryPlus.Port), with the
	// catalog the converter wrote: the bundles, the scenes in the game's build order and the Resources.Load paths.
	public static class GameContent
	{
		public const string CatalogName = "catalog.txt";
		public const string GameRootName = "game.txt";

		private static readonly List<AssetBundle> bundles = new List<AssetBundle>();
		private static readonly Dictionary<string, AssetBundle> bundlesByName = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
		private static readonly Dictionary<string, List<AssetBundle>> resources = new Dictionary<string, List<AssetBundle>>(StringComparer.Ordinal);
		private static readonly List<string> scenes = new List<string>();
		private static readonly List<UnityEngine.Object> scripts = new List<UnityEngine.Object>();

		public static bool Loaded { get; private set; }

		public static string Directory { get; private set; }

		// Scene names in the game's build order: the game's build index i is scenes[i].
		public static IList<string> Scenes => scenes;

		public static IList<AssetBundle> Bundles => bundles;

		public static void Load(string directory)
		{
			if (Loaded)
			{
				return;
			}
			Directory = directory;
			foreach (string line in File.ReadAllLines(Path.Combine(directory, CatalogName)))
			{
				if (line.StartsWith("bundle ", StringComparison.Ordinal))
				{
					string name = line.Substring(7);
					AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(directory, name));
					if (bundle == null)
					{
						throw new InvalidDataException("Could not load " + name);
					}
					bundles.Add(bundle);
					bundlesByName[name] = bundle;
				}
				else if (line.StartsWith("scene ", StringComparison.Ordinal))
				{
					string[] parts = line.Split(new[] { ' ' }, 3);
					int index = int.Parse(parts[1]);
					while (scenes.Count <= index)
					{
						scenes.Add(null);
					}
					scenes[index] = parts[2];
				}
				else if (line.StartsWith("resource ", StringComparison.Ordinal))
				{
					int split = line.LastIndexOf(' ');
					string path = line.Substring(9, split - 9);
					if (!resources.TryGetValue(path, out List<AssetBundle> list))
					{
						list = new List<AssetBundle>();
						resources[path] = list;
					}
					AssetBundle bundle = bundlesByName[line.Substring(split + 1)];
					if (!list.Contains(bundle))
					{
						list.Add(bundle);
					}
				}
			}
			// Like the 5.6 player, which had every script loaded from the start: a MonoBehaviour in a scene binds to its
			// class only when its MonoScript is already loaded. Kept referenced so they stay.
			foreach (AssetBundle bundle in bundles)
			{
				foreach (string name in bundle.GetAllAssetNames())
				{
					if (name.StartsWith("monoscript/", StringComparison.Ordinal))
					{
						scripts.Add(bundle.LoadAsset(name));
					}
				}
			}
			Loaded = true;
		}

		public static int ScriptCount => scripts.Count;

		// The bundles holding a Resources path (lower case, no extension), or null.
		public static List<AssetBundle> ResourceBundles(string path)
		{
			resources.TryGetValue(path.ToLowerInvariant(), out List<AssetBundle> list);
			return list;
		}

		// Every Resources path below a folder ("" for all).
		public static IEnumerable<KeyValuePair<string, List<AssetBundle>>> ResourcesUnder(string folder)
		{
			string prefix = folder.Length == 0 ? "" : folder.ToLowerInvariant().TrimEnd('/') + "/";
			foreach (KeyValuePair<string, List<AssetBundle>> pair in resources)
			{
				if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
				{
					yield return pair;
				}
			}
		}
	}
}
