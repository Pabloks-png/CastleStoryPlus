using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using Brix.Assets;
using Brix.Engine;
using Brix.External.Signals;
using Brix.Game;
using Brix.IO.Serialization;
using Brix.NewUI.CornerMenus;
using Brix.UI.Builder.Menu;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;
using Newtonsoft.Json;
using UnityEngine;

namespace CastleStoryPlus.Saving;

// A save of a running game no longer freezes it:
// - The quicksave and the save menu write their game objects a slice per frame, as the autosave does (SaveSlicer).
// - The game wrote a save as: copy the whole old save to the temp folder, write every file there, delete the old
//   save, copy everything back. Now the big data files (game objects, plants, trees, blocks, terrain damage...) are
//   collected on the main thread and written by a background thread into a new folder, the old save's other files
//   are copied over, and the folder then takes the old one's place at once. If writing fails the old save is kept.
// - Game objects are written without indentation (about a third smaller; the game reads both).
// Anything that reads saves (the save lists, loading, quitting) first waits for a save still being written.
[Feature(Features.FastSave, Features.FastSaveInfo)]
internal class FastSave : MonoBehaviour
{
	private static readonly string StagingRoot = Path.Combine(Paths.CachePath, "CastleStoryPlus/Saving").Replace("\\", "/") + "/";

	// Data files of a save written by the background thread: the published path and how to write it.
	private struct Job
	{
		public string Path;

		public object Data;

		public string Text;
	}

	private static List<Job> _jobs;

	private static Thread _thread;

	private static volatile string _failure;

	private static string _failedSave;

	// After a failure saves are written the game's own way for the rest of the session.
	private static bool _disabled;

	private static JsonSerializer _serializer;

	internal static bool Deferring => _jobs != null;

	internal static bool Writing => _thread != null && _thread.IsAlive;

	private static void Enable()
	{
		Plugin.Root.AddComponent<FastSave>();
		try
		{
			// Folders of a save that was being written when the game was closed.
			if (Directory.Exists(StagingRoot))
			{
				Directory.Delete(StagingRoot, recursive: true);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("FastSave: could not clear " + StagingRoot + ": " + ex.Message);
		}
	}

	private void Update()
	{
		string failure = _failure;
		if (failure == null || Writing)
		{
			return;
		}
		_failure = null;
		GameSignals.Invoke(GameSignals.OnDisplayGenericDialog, new OkDialogContainerPopulator.GenericDialog(I2Helper.TryGet("##gamemenu_savegame_failure").ToUpper(), I2Helper.TryGet("##gamemenu_savegame_failure_message") + "\n\n\"" + _failedSave + ": " + failure + "\"", new OkDialogContainerPopulator.GenericDialog.Btn[1]
		{
			new OkDialogContainerPopulator.GenericDialog.Btn(I2Helper.TryGet("##gamemenu_savegame_failure_ok").ToUpper(), () =>
			{
				GameSignals.Invoke(GameSignals.OnCloseDialog);
			}, string.Empty)
		}));
	}

	private void OnApplicationQuit()
	{
		WaitForWriting();
	}

	// Blocks until the save being written is in place.
	internal static void WaitForWriting()
	{
		Thread thread = _thread;
		if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
		{
			Stopwatch wait = Stopwatch.StartNew();
			thread.Join();
			Plugin.Log.LogInfo("FastSave: waited " + wait.ElapsedMilliseconds + " ms for the save being written");
		}
	}

	// Asset_Map.Disk_Overwrite and Disk_WriteAsNew for a saved game, with the files written in the background.
	// Returns false (and changes nothing) when the game's own save should run instead.
	internal static bool Save(Asset_Map map, bool asNew, AssetAuthorType author)
	{
		if (_disabled || map.mapType != Asset_Map.MapType.Save || map.IsStaging())
		{
			return false;
		}
		WaitForWriting();
		Stopwatch main = Stopwatch.StartNew();
		if (asNew)
		{
			map.authorType = author;
		}
		string oldFolder = map.IsShell() ? null : Folder(map._fullPath);
		string stage = StagingRoot + Path.GetRandomFileName() + "/";
		Directory.CreateDirectory(stage);
		map._stagePath = stage;
		List<Job> jobs = new List<Job>();
		_jobs = jobs;
		try
		{
			map.Disk_Save();
		}
		catch
		{
			map._stagePath = string.Empty;
			TryDelete(stage);
			throw;
		}
		finally
		{
			_jobs = null;
		}
		if (asNew)
		{
			map.path = AssetDispensary.saves.GetProperPath(map) + ((!string.IsNullOrEmpty(map.customName)) ? map.customName : Path.GetRandomFileName()) + "/";
		}
		map._stagePath = string.Empty;
		string finalFolder = Folder(map._fullPath);
		string name = map.customName;
		if (_serializer == null)
		{
			// Made here: its converters are created with it, and some are Unity types' own.
			_serializer = JsonSerializer.Create(Io.GenerateUnitySerializerSettings("save"));
		}
		long mainMs = main.ElapsedMilliseconds;
		_thread = new Thread(() => Write(jobs, stage, oldFolder, finalFolder, name, mainMs));
		_thread.IsBackground = false;
		_thread.Start();
		GameSignals.Invoke(GameSignals.GameSaved);
		GameSignals.Invoke(GameSignals.OnAutoFadeDialog, I2Helper.TryGet("##saved_game"), asNew ? 2f : 1f);
		return true;
	}

	// One of the save's data files, written by the background thread instead of now. False when not saving.
	internal static bool Defer(string path, object data, string text)
	{
		if (_jobs == null)
		{
			return false;
		}
		_jobs.Add(new Job { Path = path, Data = data, Text = text });
		return true;
	}

	private static void Write(List<Job> jobs, string stage, string oldFolder, string finalFolder, string name, long mainMs)
	{
		Stopwatch watch = Stopwatch.StartNew();
		try
		{
			foreach (Job job in jobs)
			{
				if (job.Text != null)
				{
					File.WriteAllText(job.Path, job.Text);
				}
				else
				{
					WriteJson(job.Data, job.Path);
				}
			}
			// The old save's files that this save did not write (the game copied the whole old save first).
			if (oldFolder != null && Directory.Exists(oldFolder))
			{
				CopyMissing(oldFolder, stage);
			}
			Swap(stage, finalFolder);
			Plugin.Log.LogInfo("FastSave: " + name + " saved, " + mainMs + " ms in the game, " + watch.ElapsedMilliseconds + " ms writing in the background");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("FastSave: writing " + name + " failed, the old save is kept: " + ex);
			TryDelete(stage);
			_disabled = true;
			_failedSave = name;
			_failure = ex.Message;
		}
		finally
		{
			// _thread keeps this call's closure until the next save: let go of the written texts and data now.
			jobs.Clear();
			jobs.TrimExcess();
		}
	}

	// As Asset_Base.Disk_WriteJsonData, with a serializer used only by the background thread.
	private static void WriteJson(object data, string destination)
	{
		using StreamWriter text = File.CreateText(destination);
		using JsonTextWriter json = new JsonTextWriter(text);
		_serializer.Serialize(json, data);
	}

	private static void CopyMissing(string from, string to)
	{
		foreach (string file in Directory.GetFiles(from))
		{
			string target = Path.Combine(to, Path.GetFileName(file));
			if (!File.Exists(target))
			{
				File.Copy(file, target);
			}
		}
		foreach (string folder in Directory.GetDirectories(from))
		{
			string target = Path.Combine(to, Path.GetFileName(folder));
			Directory.CreateDirectory(target);
			CopyMissing(folder, target);
		}
	}

	// The new folder takes the old one's place: two renames when both are on one drive.
	private static void Swap(string stage, string finalFolder)
	{
		string from = stage.TrimEnd('/');
		string to = finalFolder.TrimEnd('/');
		string old = null;
		if (Directory.Exists(to))
		{
			old = StagingRoot + Path.GetRandomFileName();
			if (!TryMove(to, old))
			{
				// Another drive: the game's way, delete the old save then copy the new one in.
				Directory.Delete(to, recursive: true);
				old = null;
			}
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			if (!TryMove(from, to))
			{
				Directory.CreateDirectory(to);
				CopyMissing(from, to);
				Directory.Delete(from, recursive: true);
			}
		}
		catch
		{
			if (old != null && !Directory.Exists(to))
			{
				TryMove(old, to);
				old = null;
			}
			throw;
		}
		if (old != null)
		{
			TryDelete(old);
		}
	}

	private static bool TryMove(string from, string to)
	{
		try
		{
			Directory.Move(from, to);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static void TryDelete(string folder)
	{
		try
		{
			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, recursive: true);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("FastSave: could not delete " + folder + ": " + ex.Message);
		}
	}

	private static string Folder(string path)
	{
		return path.Replace("\\", "/").TrimEnd('/') + "/";
	}
}

[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch(typeof(Asset_Map), nameof(Asset_Map.Disk_Overwrite))]
internal static class FastSaveOverwritePatch
{
	private static bool Prefix(Asset_Map __instance)
	{
		return !FastSave.Save(__instance, asNew: false, AssetAuthorType.Official);
	}
}

[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch(typeof(Asset_Map), nameof(Asset_Map.Disk_WriteAsNew))]
internal static class FastSaveWriteAsNewPatch
{
	private static bool Prefix(Asset_Map __instance, AssetAuthorType author)
	{
		return !FastSave.Save(__instance, asNew: true, author);
	}
}

// The big data files of the save are handed to the background thread. The data is the game's own fresh copy
// (arrays made for the save), except the terrain damage, which is the live dictionary and is copied here.
[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch]
internal static class FastSaveDeferPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (MethodInfo method in typeof(Asset_Map).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
		{
			if (FileName(method.Name) != null && method.GetParameters().Length == 1)
			{
				yield return method;
			}
		}
	}

	private static string FileName(string method)
	{
		switch (method)
		{
			case "Disk_WriteTrees":
				return Asset_Map.__F_TREES;
			case "Disk_WritePlants":
				return Asset_Map.__F_PLANTS;
			case "Disk_WriteBoulders":
				return Asset_Map.__F_BOULDERS;
			case "Disk_WriteIrons":
				return Asset_Map.__F_IRON;
			case "Disk_WritePyrites":
				return Asset_Map.__F_PYRITE;
			case "Disk_WriteTerrainDamage":
				return Asset_Map.__F_TERRAINDAMAGE;
			case "Disk_WritePlacedBlocks":
				return Asset_Map.__F_PLACEDBLOCKS;
			case "Disk_WriteFactionMeta":
				return Asset_Map.__F_FACTIONS;
			case "Disk_WriteGameObjects":
				return Asset_Map.__F_GAMEOBJECTS;
			default:
				return null;
		}
	}

	private static bool Prefix(Asset_Map __instance, MethodBase __originalMethod, object[] __args)
	{
		if (!FastSave.Deferring)
		{
			return true;
		}
		object data = __args[0];
		if (data is Dictionary<XYZ, float> damage)
		{
			data = new Dictionary<XYZ, float>(damage);
		}
		string path = __instance.path + FileName(__originalMethod.Name);
		return !FastSave.Defer(path, (data is string) ? null : data, data as string);
	}
}

// Reading saves waits for a save still being written: the save and load lists, loading a map, its voxels.
[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch]
internal static class FastSaveWaitPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(AssetHandler<Asset_Map>), nameof(AssetHandler<Asset_Map>.RefreshCache));
		yield return AccessTools.Method(typeof(VoxelEngine), nameof(VoxelEngine.Load));
		foreach (MethodInfo method in typeof(Asset_Map).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
		{
			if (method.Name.StartsWith("Disk_Read", StringComparison.Ordinal) || method.Name == "get_Disk_ReadPlacedBlocks" || method.Name == "Disk_EstimateTrees")
			{
				yield return method;
			}
		}
	}

	private static void Prefix()
	{
		FastSave.WaitForWriting();
	}
}

// The quicksave writes its game objects a slice per frame first.
[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch(typeof(MenuCalls), "QuickSave")]
internal static class FastSaveQuickSavePatch
{
	private static bool _running;

	private static bool Prefix(ScriptExecutionContext context, CallbackArguments args, ref DynValue __result)
	{
		if (_running || SaveSlicer.Busy)
		{
			return true;
		}
		bool started = SaveSlicer.Start("Quicksave", () =>
		{
			_running = true;
			try
			{
				MenuCalls.QuickSave(context, args);
			}
			finally
			{
				_running = false;
			}
		});
		if (!started)
		{
			return true;
		}
		__result = DynValue.Void;
		return false;
	}
}

// So does the save menu's Save button; the menu closes when the save is written, as before.
[Feature(Features.FastSave, Features.FastSaveInfo)]
[HarmonyPatch(typeof(BaseMapSaverMenu), "OnSave")]
internal static class FastSaveMenuPatch
{
	private static bool _running;

	private static bool _pending;

	private static bool Prefix(BaseMapSaverMenu __instance)
	{
		if (_running)
		{
			return true;
		}
		if (_pending)
		{
			return false;
		}
		if (SaveSlicer.Busy || BaseMapSaverMenu.SelectedAsset == null)
		{
			return true;
		}
		_pending = true;
		bool started = SaveSlicer.Start("Save", () =>
		{
			_pending = false;
			if (__instance == null)
			{
				return;
			}
			_running = true;
			try
			{
				__instance.OnSave();
			}
			finally
			{
				_running = false;
			}
		});
		if (!started)
		{
			_pending = false;
			return true;
		}
		return false;
	}
}
