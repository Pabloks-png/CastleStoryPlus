using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using MoonSharp.Interpreter;
using UnityEngine;

namespace CastleStoryPlus.Invasion;

// Corruptron waves do not spawn on the players' doorstep: a wave's spawn point (one of the map's corruptron spawn
// markers) is drawn only among the points at least [SpawnDistance] MinBlocks (60) blocks across the ground from the
// nearest block a player has built (blueprints do not count), and the farther a point is from the players' blocks the
// likelier it is drawn (weighted by the distance squared). When no point is that far, any point is drawn as before.
// The point picked ahead for the wave arrow (WaveDirection) is checked again when the wave comes, so building towards
// it moves the wave elsewhere. Host only (the spawn is picked in the invasion script on the host).
[Feature(Features.SpawnDistance, Features.SpawnDistanceInfo)]
internal static class SpawnDistance
{
	private const float CacheSeconds = 5f;

	// The default of MinBlocks before it became 60; a config still holding it is moved to the new default.
	private const int OldDefault = 30;

	private const int Default = 60;

	// The last spawn point drawn (x, z), for the developer API.
	internal static Vector2? LastPicked;

	private static ConfigEntry<int> _minBlocks;

	private static readonly System.Random Random = new System.Random();

	private static float _cachedAt = -1f;

	private static readonly List<Vector2> Built = new List<Vector2>();

	// Treasure ruins lie within this of their chest (blocks).
	private const float RuinRadius = 20f;

	private static readonly List<Vector3> Chests = new List<Vector3>();

	private static void Enable()
	{
		_minBlocks = Plugin.Cfg.Bind("SpawnDistance", "MinBlocks", Default, new ConfigDescription("Blocks (across the ground) a corruptron wave spawns at least from the nearest block a player has built, when the map has a spawn point that far; 0: off.", new AcceptableValueRange<int>(0, 500)));
		if (_minBlocks.Value == OldDefault)
		{
			_minBlocks.Value = Default;
		}
		GameSession.OnLeave(() =>
		{
			_cachedAt = -1f;
			LastPicked = null;
		});
		// CastleStoryPlus.SpawnClear(x, z): whether a spawn point is far enough from the players' blocks.
		LuaInjection.AddFunction("SpawnClear", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			if (args.Count < 2 || args[0].Type != DataType.Number || args[1].Type != DataType.Number)
			{
				return DynValue.True;
			}
			return DynValue.NewBoolean(IsClear((float)args[0].Number, (float)args[1].Number));
		});
		// CastleStoryPlus.PickSpawn({x1, z1, x2, z2, ...}): a 1-based index among the far enough points, else any.
		LuaInjection.AddFunction("PickSpawn", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			Table table = (args.Count > 0 && args[0].Type == DataType.Table) ? args[0].Table : null;
			int count = (table != null) ? table.Length / 2 : 0;
			if (count == 0)
			{
				return DynValue.Nil;
			}
			List<int> clear = new List<int>();
			for (int i = 0; i < count; i++)
			{
				if (IsClear((float)table.Get(2 * i + 1).Number, (float)table.Get(2 * i + 2).Number))
				{
					clear.Add(i + 1);
				}
			}
			if (clear.Count == 0)
			{
				Plugin.Log.LogInfo("SpawnDistance: no spawn point is " + _minBlocks.Value + " blocks from the players' blocks, a random one is used");
				int any = Random.Next(count) + 1;
				LastPicked = new Vector2((float)table.Get(2 * any - 1).Number, (float)table.Get(2 * any).Number);
				return DynValue.NewNumber(any);
			}
			int picked = Weighted(table, clear);
			float x = (float)table.Get(2 * picked - 1).Number;
			float z = (float)table.Get(2 * picked).Number;
			LastPicked = new Vector2(x, z);
			Plugin.Log.LogInfo("SpawnDistance: spawn point " + picked + " of " + count + " (" + clear.Count + " far enough) at " + Mathf.RoundToInt(x) + "," + Mathf.RoundToInt(z) + ", " + Mathf.RoundToInt(Nearest(x, z)) + " blocks from the nearest of " + Built.Count + " built blocks");
			return DynValue.NewNumber(picked);
		});
	}

	// One of the clear points (1-based), drawn with a weight of its distance to the nearest built block, squared.
	private static int Weighted(Table table, List<int> clear)
	{
		double[] weights = new double[clear.Count];
		double total = 0.0;
		for (int i = 0; i < clear.Count; i++)
		{
			float distance = Mathf.Min(Nearest((float)table.Get(2 * clear[i] - 1).Number, (float)table.Get(2 * clear[i]).Number), 1000f);
			weights[i] = (double)distance * distance;
			total += weights[i];
		}
		double roll = Random.NextDouble() * total;
		for (int i = 0; i < clear.Count; i++)
		{
			roll -= weights[i];
			if (roll <= 0.0)
			{
				return clear[i];
			}
		}
		return clear[clear.Count - 1];
	}

	// The map's corruptron spawn points for the developer API: position, blocks to the nearest built block, whether far
	// enough, and the chance each has to be drawn now.
	internal static List<object[]> Describe()
	{
		List<object[]> rows = new List<object[]>();
		List<Brix.Lua.LuaMarker> markers = Brix.Lua.LuaMarker.GetGroup("Corruptron Spawn Point");
		double total = 0.0;
		bool anyClear = false;
		foreach (Brix.Lua.LuaMarker marker in markers)
		{
			if (IsClear(marker.position.x, marker.position.z))
			{
				anyClear = true;
				float distance = Mathf.Min(Nearest(marker.position.x, marker.position.z), 1000f);
				total += (double)distance * distance;
			}
		}
		foreach (Brix.Lua.LuaMarker marker in markers)
		{
			Vector3 at = marker.position;
			float nearest = Nearest(at.x, at.z);
			bool clear = IsClear(at.x, at.z);
			double chance;
			if (!anyClear)
			{
				chance = 1.0 / markers.Count;
			}
			else if (clear && total > 0.0)
			{
				float capped = Mathf.Min(nearest, 1000f);
				chance = (double)capped * capped / total;
			}
			else
			{
				chance = 0.0;
			}
			rows.Add(new object[] { at, nearest, clear, chance });
		}
		return rows;
	}

	internal static int MinBlocks
	{
		get { return (_minBlocks != null) ? _minBlocks.Value : 0; }
	}

	internal static int BuiltBlocks
	{
		get
		{
			Refresh();
			return Built.Count;
		}
	}

	private static bool IsClear(float x, float z)
	{
		int min = _minBlocks.Value;
		if (min <= 0)
		{
			return true;
		}
		Refresh();
		float limit = (float)min * min;
		Vector2 at = new Vector2(x, z);
		foreach (Vector2 block in Built)
		{
			if ((block - at).sqrMagnitude < limit)
			{
				return false;
			}
		}
		return true;
	}

	// Blocks across the ground to the nearest built block (a large number when there is none).
	private static float Nearest(float x, float z)
	{
		Refresh();
		float best = float.MaxValue;
		Vector2 at = new Vector2(x, z);
		foreach (Vector2 block in Built)
		{
			best = Mathf.Min(best, (block - at).sqrMagnitude);
		}
		return (best == float.MaxValue) ? 99999f : Mathf.Sqrt(best);
	}

	private static bool NearChest(Vector2 at)
	{
		foreach (Vector3 chest in Chests)
		{
			if ((new Vector2(chest.x, chest.z) - at).sqrMagnitude < RuinRadius * RuinRadius)
			{
				return true;
			}
		}
		return false;
	}

	// The ground positions of the players' finished blocks, gathered at most every CacheSeconds.
	private static void Refresh()
	{
		if (_cachedAt >= 0f && Time.time - _cachedAt < CacheSeconds)
		{
			return;
		}
		_cachedAt = Time.time;
		Built.Clear();
		Faction neutral = Faction.DefaultNeutral;
		Chests.Clear();
		Treasure.TreasureChests.ChestPositions(Chests);
		foreach (IVolume volume in VolumeRegistry._data.RegisteredVolumesData.Keys)
		{
			// Some registered volumes have no game object (or no validation data) any more: skipped.
			GameObject go = (volume != null) ? volume.GameObject : null;
			if (go == null || go.GetComponent<Blueprint>() != null)
			{
				continue;
			}
			Faction faction;
			try
			{
				faction = volume.Faction;
			}
			catch (System.NullReferenceException)
			{
				continue;
			}
			// A built block carries no team of its own (no faction, or the neutral one): it counts unless it is part
			// of a treasure ruin. The enemies' things never count.
			if (faction != null && faction.isAI)
			{
				continue;
			}
			XYZ position = volume.Position;
			Vector2 at = new Vector2(position.x + 0.5f, position.z + 0.5f);
			if ((faction == null || faction == neutral) && NearChest(at))
			{
				continue;
			}
			Built.Add(at);
		}
	}
}
