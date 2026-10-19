using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Brix.IO.Serialization;
using Brix.Utils;
using CastleStoryPlus.Core;
using CastleStoryPlus.Loading;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace CastleStoryPlus.Saving;

// Writes the game objects of a save a slice per frame instead of in one frame, for the autosave and (with FastSave)
// the quicksave and save menu. The simulation is paused meanwhile, so all objects are saved from the same moment,
// and the result is handed to the game's own save through GameObjectSerializer.GetJsonGameState.
[Feature]
internal class SaveSlicer : MonoBehaviour
{
	// Time per frame for writing game objects; the game keeps drawing in between. The simulation is paused, so
	// a longer slice only makes the pause shorter.
	private const long SliceMilliseconds = 25;

	private static SaveSlicer _instance;

	private static int _lastGameObjectsLength;

	// Game objects written ahead, used by the next GameObjectSerializer.GetJsonGameState("save") call.
	internal static string PreparedGameObjects;

	// A sliced save is being written (over several frames).
	internal static bool Busy { get; private set; }

	private static void Enable()
	{
		_instance = Plugin.Root.AddComponent<SaveSlicer>();
	}

	// Writes the game objects over several frames, then runs save (the game's own save code) in one frame with
	// them prepared. Returns false when another sliced save is running.
	internal static bool Start(string label, Action save)
	{
		if (Busy || _instance == null)
		{
			return false;
		}
		Busy = true;
		_instance.StartCoroutine(Run(label, save));
		return true;
	}

	private static IEnumerator Run(string label, Action save)
	{
		float timeScale = Time.timeScale;
		Time.timeScale = 0f;
		Stopwatch total = Stopwatch.StartNew();
		int frames = 0;
		int gcs = GC.CollectionCount(0);
		long allocated = 0;
		string objects = null;
		IEnumerator writing = WriteGameObjects(SliceMilliseconds, json => objects = json);
		while (true)
		{
			bool more;
			long before = GC.GetTotalMemory(false);
			try
			{
				more = writing.MoveNext();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError(label + ": writing game objects failed, using the game's own save: " + ex);
				objects = null;
				break;
			}
			finally
			{
				allocated += Math.Max(0, GC.GetTotalMemory(false) - before);
			}
			if (!more)
			{
				break;
			}
			frames++;
			yield return null;
		}
		long sliced = total.ElapsedMilliseconds;
		Stopwatch write = Stopwatch.StartNew();
		PreparedGameObjects = objects;
		long beforeSave = GC.GetTotalMemory(false);
		try
		{
			save();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError(label + " failed: " + ex);
		}
		finally
		{
			PreparedGameObjects = null;
			allocated += Math.Max(0, GC.GetTotalMemory(false) - beforeSave);
			if (Time.timeScale == 0f)
			{
				Time.timeScale = timeScale;
			}
			Busy = false;
		}
		// Allocated: the heap's growth within the slices and the save (a slice with a collection counts too little).
		Plugin.Log.LogInfo(label + " timing: game objects " + sliced + " ms over " + (frames + 1) + " frames, rest of the save " + write.ElapsedMilliseconds + " ms in one frame, about " + (allocated / 1048576) + " MB allocated, " + (GC.CollectionCount(0) - gcs) + " garbage collections" + TopAllocations(8));
	}

	private sealed class Allocation
	{
		public string Kind;

		public long Bytes;

		public int Count;
	}

	// Heap growth per kind of game object (its name without "(Clone)" and numbers) during the last sliced save.
	private static readonly Dictionary<string, Allocation> Allocations = new Dictionary<string, Allocation>();

	private static void CountAllocation(SerializationComponent item, long bytes)
	{
		// A collection during the item makes the heap shrink; that item is left out.
		if (bytes <= 0 || item == null)
		{
			return;
		}
		string kind = Kind(item.gameObject.name);
		if (!Allocations.TryGetValue(kind, out Allocation allocation))
		{
			allocation = new Allocation { Kind = kind };
			Allocations[kind] = allocation;
		}
		allocation.Bytes += bytes;
		allocation.Count++;
	}

	private static string Kind(string name)
	{
		int end = name.IndexOf("(Clone)", StringComparison.Ordinal);
		if (end < 0)
		{
			end = name.Length;
		}
		while (end > 0 && (char.IsDigit(name[end - 1]) || name[end - 1] == ' ' || name[end - 1] == '_'))
		{
			end--;
		}
		return (end > 0) ? name.Substring(0, end) : name;
	}

	// The kinds of game objects whose writing allocated the most, for the timing line.
	private static string TopAllocations(int count)
	{
		List<Allocation> list = new List<Allocation>(Allocations.Values);
		list.Sort((Allocation a, Allocation b) => b.Bytes.CompareTo(a.Bytes));
		StringBuilder text = new StringBuilder();
		for (int i = 0; i < list.Count && i < count; i++)
		{
			text.Append((i == 0) ? "; most allocated by " : ", ").Append(list[i].Kind).Append(' ').Append(list[i].Bytes / 1048576).Append(" MB (").Append(list[i].Count).Append("x)");
		}
		return text.ToString();
	}

	// Same output as GameObjectSerializer.GetJsonGameState("save"), a slice per frame. One serializer and writer
	// for all objects, so references between objects are written exactly as in one go. With FastSave the JSON is
	// written without indentation (the game reads both).
	internal static IEnumerator WriteGameObjects(long sliceMilliseconds, Action<string> done)
	{
		GameObjectSerializer gameObjects = UnityEngine.Object.FindObjectOfType<GameObjectSerializer>();
		if (gameObjects == null)
		{
			yield break;
		}
		List<SerializationComponent> queue = new List<SerializationComponent>(gameObjects._saveQueue);
		// Sized from the last save: growing a ~6 MB builder by doubling leaves ~20 MB of dead buffers per save.
		StringBuilder text = new StringBuilder(Mathf.Max(1024, _lastGameObjectsLength + _lastGameObjectsLength / 8));
		text.Append("[");
		JsonSerializer serializer = JsonSerializer.Create(Io.GetSaveSerializerSettings("save"));
		if (Plugin.IsEnabled(Features.FastSave))
		{
			serializer.Formatting = Formatting.None;
		}
		StringWriter writer = new StringWriter(text);
		FrameBudget budget = new FrameBudget(sliceMilliseconds);
		Allocations.Clear();
		foreach (SerializationComponent item in queue)
		{
			if (item != null)
			{
				long before = GC.GetTotalMemory(false);
				item.Save(writer, serializer);
				CountAllocation(item, GC.GetTotalMemory(false) - before);
			}
			if (budget.ExceededNow)
			{
				yield return null;
				budget.Reset();
			}
		}
		text.Remove(text.Length - 1, 1);
		text.Append("]");
		_lastGameObjectsLength = text.Length;
		done(text.ToString());
	}
}

// Hands the game objects written ahead to the game's save. A save that was not sliced (save and quit, the world
// editor's saves are left alone) is still written compact in one go with FastSave.
[Feature]
[HarmonyPatch(typeof(GameObjectSerializer), nameof(GameObjectSerializer.GetJsonGameState))]
internal static class SaveSlicerPreparedObjectsPatch
{
	private static bool Prefix(string target, ref string __result)
	{
		if (target != "save")
		{
			return true;
		}
		if (SaveSlicer.PreparedGameObjects != null)
		{
			__result = SaveSlicer.PreparedGameObjects;
			SaveSlicer.PreparedGameObjects = null;
			return false;
		}
		if (!Plugin.IsEnabled(Features.FastSave))
		{
			return true;
		}
		string objects = null;
		IEnumerator writing = SaveSlicer.WriteGameObjects(long.MaxValue, json => objects = json);
		while (writing.MoveNext())
		{
		}
		if (objects == null)
		{
			return true;
		}
		__result = objects;
		return false;
	}
}
