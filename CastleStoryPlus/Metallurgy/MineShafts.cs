using System;
using System.Collections.Generic;
using System.Reflection;
using Brix.Engine;
using Brix.Engine.FlatWorld.Layers;
using Brix.Game.AI.Damage;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Metallurgy;

// Mine shafts: 2 to 4 big lodes of ore in the deep rock of every map, where an island's cliff cuts through them, so
// they show on the cliff face. A lode is a lumpy ellipsoid (about 1000 to 1500 blocks) of orange crystal, blue
// crystal, iron and coal in chunks, without any stone, and its blocks take HardnessMultiplier times as long to dig.
// Like the coal and blue crystal veins, the deep rock is not saved: every machine places the lodes from the map's
// seed and heightmap each time a map is opened, so old maps and saves get them too, wherever nobody has dug yet.
// Blue crystal is only used with BlueCrystalDeposits on (otherwise it cannot be carried), coal only with Metallurgy.
[Feature(Features.MineShafts, Features.MineShaftsInfo)]
[HarmonyPatch]
internal static class MineShafts
{
	internal const float HardnessMultiplier = 3f;

	private const int OrangeType = (int)VoxelTerrainType.OrangeCrystal;

	private const int BlueType = (int)VoxelTerrainType.BlueCrystal;

	private const int IronType = (int)VoxelTerrainType.Iron;

	// Points a lode's chunks grow from: every block takes the type of the nearest one.
	private const int ChunkPoints = 48;

	private const int Tries = 30000;

	// Lodes keep this far apart (in columns); the second half of the tries allows half of it.
	private const int Separation = 90;

	private sealed class Lode
	{
		public int X;

		public int Y;

		public int Z;

		public int Rx;

		public int Ry;

		public int Rz;

		public int Seed;

		public Vector3[] Points;

		public int[] Types;

		public bool Bounds(XYZ p)
		{
			return p.x >= X - Rx - 2 && p.x <= X + Rx + 2 && p.y >= Y - Ry - 2 && p.y <= Y + Ry + 2 && p.z >= Z - Rz - 2 && p.z <= Z + Rz + 2;
		}

		// Inside the ellipsoid, with an edge that wobbles by up to a third of the radius.
		public bool Contains(XYZ p)
		{
			if (!Bounds(p))
			{
				return false;
			}
			float dx = (float)(p.x - X) / Rx;
			float dy = (float)(p.y - Y) / Ry;
			float dz = (float)(p.z - Z) / Rz;
			float d = dx * dx + dy * dy + dz * dz;
			return d <= 0.7f + 0.6f * Rand01(p.x >> 1, p.y >> 1, p.z >> 1, Seed);
		}

		public int TypeAt(XYZ p)
		{
			int best = 0;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < Points.Length; i++)
			{
				float dx = p.x - Points[i].x;
				float dy = p.y - Points[i].y;
				float dz = p.z - Points[i].z;
				float distance = dx * dx + dy * dy + dz * dz;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}
			return Types[best];
		}
	}

	// Lodes of one world (its deep layer) and seed.
	private sealed class State
	{
		public object Layer;

		public int Seed;

		public Lode[] Lodes;
	}

	private static readonly object Sync = new object();

	// Read from the terrain threads, replaced whole.
	private static volatile State _state;

	private static MethodBase TargetMethod()
	{
		// The game's own DeepLayer (Assembly-CSharp-firstpass); Brix.Core has an unused one of the same name.
		Type deepLayer = typeof(VoxelEngineLayers).Assembly.GetType("Brix.Engine.FlatWorld.Layers.DeepLayer");
		return AccessTools.Method(deepLayer, "GetDeepType", new[] { typeof(Vector3), typeof(Color32) });
	}

	private static void Postfix(object __instance, Vector3 voxelPos, ref VoxelTerrainType __result)
	{
		Lode[] lodes = LodesFor(__instance);
		if (lodes == null)
		{
			return;
		}
		XYZ p = XYZ.FromVector3(voxelPos);
		for (int i = 0; i < lodes.Length; i++)
		{
			if (lodes[i].Contains(p))
			{
				__result = (VoxelTerrainType)lodes[i].TypeAt(p);
				return;
			}
		}
	}

	// Whether a terrain block of this type at p is part of a lode (blocks of the surface layers are not ore, so they
	// are left out even inside a lode's shape).
	internal static bool IsLodeBlock(XYZ p, int type)
	{
		State state = _state;
		if (state == null || state.Lodes == null || !IsOre(type))
		{
			return false;
		}
		for (int i = 0; i < state.Lodes.Length; i++)
		{
			if (state.Lodes[i].Contains(p))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsOre(int type)
	{
		return type == OrangeType || type == BlueType || type == IronType || type == CoalVeins.CoalType;
	}

	private static Lode[] LodesFor(object layer)
	{
		if (!VoxelNoiseConfigurator.IsInitialized())
		{
			return null;
		}
		int seed = VoxelNoiseConfigurator.GetGlobalSeed();
		State state = _state;
		if (state != null && state.Layer == layer && state.Seed == seed)
		{
			return state.Lodes;
		}
		lock (Sync)
		{
			state = _state;
			if (state != null && state.Layer == layer && state.Seed == seed)
			{
				return state.Lodes;
			}
			HeightmapLayer heightmap = (Voxel.Engine is LayerVoxelEngine layered && layered.test != null && layered.test.layers != null) ? layered.test.layers.GetLayer<HeightmapLayer>() : null;
			if (heightmap == null || heightmap.fillingTex == null || heightmap.fillingTex.Width == 0)
			{
				return null;
			}
			Lode[] lodes;
			try
			{
				lodes = Place(heightmap.fillingTex.GetPixels32(), heightmap.fillingTex.Width, heightmap.fillingTex.Height, seed);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("MineShafts: could not place the lodes: " + ex);
				lodes = new Lode[0];
			}
			_state = new State { Layer = layer, Seed = seed, Lodes = lodes };
			return lodes;
		}
	}

	// Columns are (bottom, top) in r and g, x + z * width.
	private static Lode[] Place(Color32[] columns, int width, int height, int seed)
	{
		System.Random random = new System.Random(seed * 7919 + width * 31 + height);
		int count = 2 + random.Next(3);
		List<Lode> lodes = new List<Lode>();
		List<int> types = OreTypes();
		for (int attempt = 0; attempt < Tries && lodes.Count < count; attempt++)
		{
			int x = random.Next(width);
			int z = random.Next(height);
			int rx = 7 + random.Next(3);
			int rz = 7 + random.Next(3);
			int ry = 4 + random.Next(2);
			int depth = ry + 4 + random.Next(4);
			Color32 column = columns[x + z * width];
			if (column.g <= column.r || column.g - column.r < 2 * ry + 10)
			{
				continue;
			}
			int y = column.g - 1 - depth;
			if (y - ry < column.r + 2)
			{
				continue;
			}
			int separation = (attempt < Tries / 2) ? Separation : Separation / 2;
			if (lodes.Exists((Lode l) => (l.X - x) * (l.X - x) + (l.Z - z) * (l.Z - z) < separation * separation))
			{
				continue;
			}
			if (!OnCliff(columns, width, height, x, y, z, Mathf.Min(rx, rz), ry))
			{
				continue;
			}
			Lode lode = new Lode { X = x, Y = y, Z = z, Rx = rx, Ry = ry, Rz = rz, Seed = random.Next() };
			lode.Points = new Vector3[ChunkPoints];
			lode.Types = new int[ChunkPoints];
			for (int i = 0; i < ChunkPoints; i++)
			{
				lode.Points[i] = new Vector3(x + rx * (2f * (float)random.NextDouble() - 1f), y + ry * (2f * (float)random.NextDouble() - 1f), z + rz * (2f * (float)random.NextDouble() - 1f));
				lode.Types[i] = types[random.Next(types.Count)];
			}
			lodes.Add(lode);
			Plugin.Log.LogInfo("MineShafts: lode at " + x + ", " + y + ", " + z + " (" + (2 * rx + 1) + " x " + (2 * ry + 1) + " x " + (2 * rz + 1) + ")");
		}
		if (lodes.Count < count)
		{
			Plugin.Log.LogInfo("MineShafts: room for " + lodes.Count + " of " + count + " lodes on this map");
		}
		return lodes.ToArray();
	}

	// Weighted: orange crystal and iron 3, blue crystal and coal 2.
	private static List<int> OreTypes()
	{
		List<int> types = new List<int> { OrangeType, OrangeType, OrangeType, IronType, IronType, IronType };
		if (Plugin.IsEnabled(Features.BlueCrystalDeposits))
		{
			types.Add(BlueType);
			types.Add(BlueType);
		}
		if (Plugin.IsEnabled(Features.Metallurgy))
		{
			types.Add(CoalVeins.CoalType);
			types.Add(CoalVeins.CoalType);
		}
		return types;
	}

	// The lode must stick out of a cliff on one side: some columns around its edge are empty (or end below it), the
	// columns half way to the edge all hold it, so it is neither buried nor on a thin spire.
	private static bool OnCliff(Color32[] columns, int width, int height, int x, int y, int z, int r, int ry)
	{
		int open = 0;
		for (int i = 0; i < 16; i++)
		{
			float angle = i * Mathf.PI / 8f;
			int ex = x + Mathf.RoundToInt(Mathf.Cos(angle) * (r - 2));
			int ez = z + Mathf.RoundToInt(Mathf.Sin(angle) * (r - 2));
			if (!Holds(columns, width, height, ex, ez, y))
			{
				open++;
			}
			if (i % 4 == 0)
			{
				int hx = x + Mathf.RoundToInt(Mathf.Cos(angle) * r / 2f);
				int hz = z + Mathf.RoundToInt(Mathf.Sin(angle) * r / 2f);
				if (!Holds(columns, width, height, hx, hz, y + ry))
				{
					return false;
				}
			}
		}
		return open >= 2 && open <= 7;
	}

	private static bool Holds(Color32[] columns, int width, int height, int x, int z, int y)
	{
		if (x < 0 || z < 0 || x >= width || z >= height)
		{
			return false;
		}
		Color32 column = columns[x + z * width];
		return column.g > column.r && y >= column.r && y < column.g;
	}

	internal static float Rand01(int x, int y, int z, int seed)
	{
		unchecked
		{
			uint h = (uint)seed * 0x9E3779B1u;
			h ^= (uint)x * 0x85EBCA77u;
			h = (h << 13 | h >> 19) * 5u + 0xE6546B64u;
			h ^= (uint)y * 0xC2B2AE3Du;
			h = (h << 13 | h >> 19) * 5u + 0xE6546B64u;
			h ^= (uint)z * 0x27D4EB2Fu;
			h ^= h >> 15;
			h *= 0x2C1B3C6Du;
			h ^= h >> 12;
			h *= 0x297A2D39u;
			h ^= h >> 15;
			return (h & 0xFFFFFF) / 16777216f;
		}
	}
}

// Lode blocks take longer to dig.
[Feature(Features.MineShafts, Features.MineShaftsInfo)]
[HarmonyPatch(typeof(TerrainDamageReceiver), "SetMaxHPAndOverkillFromFactory")]
internal static class MineShaftHardnessPatch
{
	private static void Postfix(TerrainDamageReceiver __instance, TerrainBlockData tbd)
	{
		if (tbd != null && tbd.BlockInfo != null && MineShafts.IsLodeBlock(tbd.Position, tbd.BlockInfo.ID))
		{
			__instance.MaxHP *= MineShafts.HardnessMultiplier;
		}
	}
}
