using System;
using System.IO;
using CastleStoryPlus.Compat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleStoryPlus.Player
{
	// Starts the game from its converted content: loads the bundles, routes Resources.Load to them and opens the game's
	// first scene. The content is made from the player's own Castle Story install; without it nothing of the game runs.
	public static class Bootstrap
	{
		public static string ContentDirectory
		{
			get
			{
				string dir = Environment.GetEnvironmentVariable("CSP_CONTENT");
				return string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content")) : dir;
			}
		}

		// Castle Story's Steam app id: Steam starts the game with it set, this player is started on its own.
		private const string SteamAppId = "227860";

		// Before any game code: the game uses paths relative to its own folder (Info/...), and Steam needs the app id.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Prepare()
		{
			// CSP_GAME_ROOT: another folder to run in (development: a copy of Info, away from the real saves).
			string root = Environment.GetEnvironmentVariable("CSP_GAME_ROOT");
			string rootFile = Path.Combine(ContentDirectory, GameContent.GameRootName);
			if (string.IsNullOrEmpty(root) && File.Exists(rootFile))
			{
				root = File.ReadAllText(rootFile).Trim();
			}
			if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
			{
				Directory.SetCurrentDirectory(root);
			}
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SteamAppId")))
			{
				Environment.SetEnvironmentVariable("SteamAppId", SteamAppId);
				Environment.SetEnvironmentVariable("SteamGameId", SteamAppId);
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Start()
		{
			string dir = ContentDirectory;
			if (!IsInstalled(dir))
			{
				Debug.Log("[CSP] No complete game content in " + dir + ", starting the installer");
				InstallerScreen.Show(dir);
				if (Environment.GetEnvironmentVariable("CSP_DIAG") == "1")
				{
					Diagnostics.StartScreenshots();
				}
				return;
			}
			float start = Time.realtimeSinceStartup;
			GameContent.Load(dir);
			ContentResources.Install();
			Debug.Log("[CSP] " + GameContent.Bundles.Count + " bundles, " + GameContent.ScriptCount + " scripts loaded in " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("0") + " ms");
			if (Environment.GetEnvironmentVariable("CSP_DIAG") == "1")
			{
				Diagnostics.LogAssemblies();
				Diagnostics.StartScreenshots();
				Diagnostics.LogScripts();
				Diagnostics.LogPrefab("UI/NewGame/TextBox");
				Diagnostics.LogPrefab("Prefabs/Courbes/Courbes");
				SceneManager.sceneLoaded += (scene, mode) => Diagnostics.LogScene(scene);
			}
			ModLoader.Start();
			PlayTest.StartIfAsked();
			string first = Environment.GetEnvironmentVariable("CSP_SCENE");
			SceneManager.LoadScene(string.IsNullOrEmpty(first) ? GameContent.Scenes[0] : first);
		}

		// The content is complete and unchanged since it was made: every file the installer listed (installed.sha256,
		// "<sha256> <size> <file>") is there with its size. The same check as CastleStoryPlus.Port's
		// Installer.IsInstalled, which the player does not load unless it installs.
		private static bool IsInstalled(string dir)
		{
			string list = Path.Combine(dir, "installed.sha256");
			if (!File.Exists(list) || !File.Exists(Path.Combine(dir, GameContent.CatalogName)))
			{
				return false;
			}
			// A new player (an update unpacked over the old one) has its own script assemblies list, without the game's.
			string scripts = Path.Combine(Application.dataPath, "ScriptingAssemblies.json");
			if (!File.Exists(scripts) || !File.ReadAllText(scripts).Contains("\"Assembly-CSharp.dll\""))
			{
				return false;
			}
			foreach (string line in File.ReadAllLines(list))
			{
				string[] parts = line.Split(new[] { ' ' }, 3);
				if (parts.Length < 3 || parts[0] == "manifest")
				{
					continue;
				}
				var file = new FileInfo(Path.Combine(dir, parts[2]));
				if (!file.Exists || file.Length.ToString() != parts[1])
				{
					return false;
				}
			}
			return true;
		}
	}
}
