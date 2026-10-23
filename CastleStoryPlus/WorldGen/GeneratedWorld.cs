using System.Collections.Generic;

namespace CastleStoryPlus.WorldGen;

internal enum WorldStyle
{
	Archipelago,
	Continent,
	Highlands
}

internal enum WorldSize
{
	Small,
	Medium,
	Large
}

internal enum WorldBiome
{
	Green,
	Winter,
	Desert
}

internal enum WorldAmount
{
	None,
	Few,
	Normal,
	Many,
	Lots
}

// What a random world is made from: the same settings always make the same world.
internal struct WorldSettings
{
	// Any text, normally a UUID.
	public string Seed;

	public WorldStyle Style;

	public WorldSize Size;

	public WorldBiome Biome;

	public WorldAmount Trees;

	public WorldAmount Boulders;

	// Iron and pyrite on the surface.
	public WorldAmount SurfaceOre;

	// Iron, crystal and coal in the deep rock (applied when the world loads).
	public WorldAmount DeepOre;

	// The seed as a number for the noises and random numbers: FNV-1a of the trimmed, lower case text, the same on every
	// machine (string.GetHashCode is not).
	public int SeedValue
	{
		get
		{
			uint hash = 2166136261u;
			foreach (char c in (Seed ?? string.Empty).Trim().ToLowerInvariant())
			{
				hash = unchecked((hash ^ c) * 16777619u);
			}
			return (int)(hash & 0x7FFFFFFF);
		}
	}

	public static float Multiplier(WorldAmount amount)
	{
		switch (amount)
		{
			case WorldAmount.None:
				return 0f;
			case WorldAmount.Few:
				return 0.4f;
			case WorldAmount.Many:
				return 1.8f;
			case WorldAmount.Lots:
				return 3f;
			default:
				return 1f;
		}
	}

	public override string ToString()
	{
		return Style + " " + Size + " " + Biome + " trees " + Trees + " boulders " + Boulders + " surface ore " + SurfaceOre + " deep ore " + DeepOre + " seed " + Seed;
	}
}

// A generated map: one solid column per (x, z) like the game's Heightmap.png, and what grows and stands on it.
// Columns are indexed x + z * Size in world coordinates.
internal sealed class GeneratedWorld
{
	public const int Size = 1024;

	public WorldSettings Settings;

	// Lowest solid y (inclusive) and the y above the top (exclusive); Top 0 is an empty column.
	public readonly byte[] Bottom = new byte[Size * Size];

	public readonly byte[] Top = new byte[Size * Size];

	// Terrain type of the top voxel (the heightmap's blue channel).
	public readonly byte[] Crust = new byte[Size * Size];

	// The heightmap's alpha: 255 a thin top voxel, 254 a normal one, 253 a full one (smooth steps).
	public readonly byte[] Fill = new byte[Size * Size];

	// Columns covered by a forest (darker in the thumbnail).
	public readonly bool[] Forest = new bool[Size * Size];

	// Columns of the grass routes between the starting points, crystals, spawn points and islands.
	public readonly bool[] Route = new bool[Size * Size];

	public readonly List<Tree> Trees = new List<Tree>();

	public readonly List<Plant> Plants = new List<Plant>();

	public readonly List<Boulder> Boulders = new List<Boulder>();

	public readonly List<Mineral> Iron = new List<Mineral>();

	public readonly List<Mineral> Pyrite = new List<Mineral>();

	public readonly List<Marker> Markers = new List<Marker>();

	public bool Solid(int x, int z)
	{
		return x >= 0 && z >= 0 && x < Size && z < Size && Top[x + z * Size] != 0;
	}

	// The y of the top solid voxel, where nature and markers stand.
	public int Surface(int x, int z)
	{
		return Top[x + z * Size] - 1;
	}

	public int Count(string group)
	{
		int count = 0;
		foreach (Marker marker in Markers)
		{
			if (marker.Group == group)
			{
				count++;
			}
		}
		return count;
	}

	internal struct Tree
	{
		public int X;

		public int Y;

		public int Z;

		public int Size;
	}

	internal struct Plant
	{
		public int X;

		public int Y;

		public int Z;

		public float Growth;

		public int Type;
	}

	internal struct Boulder
	{
		// The middle of the boulder: a cell centre for a 3x3 one, a cell corner for a 2x2 one.
		public float X;

		public float Y;

		public float Z;

		public float OffsetX;

		public float OffsetZ;

		public float Growth;

		// 4 (2x2) or 9 (3x3) cells.
		public int Cells;
	}

	internal struct Mineral
	{
		public int X;

		public int Y;

		public int Z;

		public float Growth;
	}

	internal struct Marker
	{
		public string Group;

		public int X;

		public int Y;

		public int Z;
	}
}
