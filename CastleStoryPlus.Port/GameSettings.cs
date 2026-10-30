using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace CastleStoryPlus.Port;

// Copies the game's project settings (tags and layers, physics, time, input, quality, audio, nav mesh areas) from the
// player's own install into the Unity 6 player's globalgamemanagers. The player is built with Unity's default settings,
// so none of the game's settings are in what is distributed.
//
// Fields are matched by name (FieldCopy). A manager whose Unity 6 layout does not match the
// class database is left as it is and reported.
public static class GameSettings
{
	public const string OriginalName = "globalgamemanagers.player";
	public const string PatchedHashName = "globalgamemanagers.sha256";
	// Written by the player build: Unity 6's default settings and a shader, with their type trees: the exact layouts of
	// the player's managers and shaders (the class database only has those of an earlier Unity 6).
	public const string TypesName = "engine-types.assets";

	private static readonly int[] Managers =
	{
		5,   // TimeManager
		11,  // AudioManager
		13,  // InputManager
		19,  // Physics2DSettings
		47,  // QualitySettings
		55,  // PhysicsManager
		78,  // TagManager
		126, // NavMeshProjectSettings
	};

	public static void Copy(string classPackage, string gameDataDir, string playerDataDir, Action<string> log)
	{
		string target = Path.Combine(playerDataDir, "globalgamemanagers");
		string original = Path.Combine(playerDataDir, OriginalName);
		string patchedHash = Path.Combine(playerDataDir, PatchedHashName);
		// Always start from the player's own file, so installing again gives the same result. A file other than the one
		// written last is a new player's own.
		if (!File.Exists(original) || !File.Exists(patchedHash) || File.ReadAllText(patchedHash).Trim() != GameFiles.Hash(target))
		{
			File.Copy(target, original, true);
		}

		var gameManager = new AssetsManager();
		gameManager.LoadClassPackage(classPackage);
		gameManager.LoadClassDatabaseFromPackage(BundleConverter.EngineVersion);
		AssetsFileInstance game = gameManager.LoadAssetsFile(Path.Combine(gameDataDir, "globalgamemanagers"), false);

		var playerManager = new AssetsManager();
		playerManager.LoadClassPackage(classPackage);
		AssetsFileInstance player = playerManager.LoadAssetsFile(original, false);
		playerManager.LoadClassDatabaseFromPackage(player.file.Metadata.UnityVersion);
		var playerTypes = new Dictionary<int, TypeTreeType>();
		string typesPath = Path.Combine(playerDataDir, TypesName);
		if (File.Exists(typesPath))
		{
			AssetsFileInstance types = playerManager.LoadAssetsFile(typesPath, false);
			foreach (TypeTreeType type in types.file.Metadata.TypeTreeTypes)
			{
				playerTypes[type.TypeId] = type;
			}
		}

		int copied = 0;
		foreach (int classId in Managers)
		{
			AssetFileInfo from = game.file.GetAssetsOfType(classId).FirstOrDefault();
			AssetFileInfo to = player.file.GetAssetsOfType(classId).FirstOrDefault();
			if (from == null || to == null)
			{
				log("settings: class " + classId + " missing in " + (from == null ? "game" : "player"));
				continue;
			}
			AssetTypeValueField source = Read(game, from, gameManager.GetTemplateBaseField(game, from));
			AssetTypeValueField destination = Read(player, to, PlayerTemplate(playerManager, player, to, playerTypes));
			if (source == null || destination == null)
			{
				log("settings: " + (source == null ? "game's " : "player's ") + "class " + classId + " does not match its layout, kept as it is");
				continue;
			}
			FieldCopy.Copy(source, destination);
			to.SetNewData(destination);
			copied++;
		}

		string temp = target + ".tmp";
		using (var writer = new AssetsFileWriter(temp))
		{
			player.file.Write(writer);
		}
		player.file.Close();
		game.file.Close();
		Installer.MoveInto(temp, target);
		File.WriteAllText(patchedHash, GameFiles.Hash(target));
		log("settings: " + copied + " of " + Managers.Length + " managers copied from the game");
	}

	private static AssetTypeTemplateField PlayerTemplate(AssetsManager manager, AssetsFileInstance inst, AssetFileInfo info, Dictionary<int, TypeTreeType> types)
	{
		if (!types.TryGetValue(info.TypeId, out TypeTreeType type))
		{
			return manager.GetTemplateBaseField(inst, info);
		}
		var template = new AssetTypeTemplateField();
		template.FromTypeTree(type);
		return template;
	}

	// The object read with a layout, or null when that layout does not read exactly the object's bytes.
	private static AssetTypeValueField Read(AssetsFileInstance inst, AssetFileInfo info, AssetTypeTemplateField template)
	{
		AssetsFileReader reader = inst.file.Reader;
		long start = info.GetAbsoluteByteOffset(inst.file);
		reader.Position = start;
		try
		{
			AssetTypeValueField field = template.MakeValue(reader);
			if (Environment.GetEnvironmentVariable("CSP_SETTINGS_DEBUG") == "1")
			{
				Console.WriteLine("read " + info.TypeId + ": " + (reader.Position - start) + " of " + info.ByteSize + " fields " + string.Join(",", template.Children.Select(c => c.Name)));
			}
			return reader.Position == start + info.ByteSize ? field : null;
		}
		catch (Exception e)
		{
			if (Environment.GetEnvironmentVariable("CSP_SETTINGS_DEBUG") == "1")
			{
				Console.WriteLine("read " + info.TypeId + ": " + e.Message);
			}
			return null;
		}
	}
}
