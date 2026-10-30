using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CastleStoryPlus.Player
{
	// Finds the player's own Castle Story install for the first start: in CSP_GAME_DATA, or in Steam's libraries.
	public static class GameLocator
	{
		public const string GameFolder = "Castle Story";
		public const string DataFolder = "Castle Story_Data";

		// The game's data folder for a folder the player gave: the game's folder or its data folder; null if neither.
		public static string DataFolderOf(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return null;
			}
			path = path.Trim().Trim('"');
			if (Directory.Exists(Path.Combine(path, DataFolder)))
			{
				return Path.GetFullPath(Path.Combine(path, DataFolder));
			}
			if (File.Exists(Path.Combine(path, "globalgamemanagers")) && Directory.Exists(Path.Combine(path, "Managed")))
			{
				return Path.GetFullPath(path);
			}
			return null;
		}

		// The game's folder, or null.
		public static string Find()
		{
			string given = DataFolderOf(Environment.GetEnvironmentVariable("CSP_GAME_DATA"));
			if (given != null)
			{
				return Path.GetDirectoryName(given);
			}
			foreach (string library in SteamLibraries())
			{
				string game = Path.Combine(library, "steamapps", "common", GameFolder);
				if (DataFolderOf(game) != null)
				{
					return game;
				}
			}
			return null;
		}

		private static IEnumerable<string> SteamLibraries()
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string root in SteamRoots())
			{
				if (!Directory.Exists(root) || !seen.Add(Path.GetFullPath(root)))
				{
					continue;
				}
				yield return root;
				string folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
				if (!File.Exists(folders))
				{
					continue;
				}
				foreach (Match match in Regex.Matches(File.ReadAllText(folders), "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
				{
					string library = match.Groups[1].Value.Replace("\\\\", "\\");
					if (Directory.Exists(library) && seen.Add(Path.GetFullPath(library)))
					{
						yield return library;
					}
				}
			}
		}

		private static IEnumerable<string> SteamRoots()
		{
			if (Application.platform == RuntimePlatform.WindowsPlayer)
			{
				string registered = null;
				try
				{
					registered = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
				}
				catch (Exception e)
				{
					Debug.LogWarning("[CSP] Steam's folder not in the registry: " + e.Message);
				}
				if (!string.IsNullOrEmpty(registered))
				{
					yield return registered.Replace('/', '\\');
				}
				yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
				yield break;
			}
			string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			yield return Path.Combine(home, ".local", "share", "Steam");
			yield return Path.Combine(home, ".steam", "steam");
			yield return Path.Combine(home, ".steam", "root");
			yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
		}
	}
}
