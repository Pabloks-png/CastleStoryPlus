using System;
using System.Collections.Generic;
using CastleStoryPlus.Core;

namespace CastleStoryPlus.WorldGen;

// Makes a random world from a seed with noise, shaped like the game's own maps (measured on them):
// - floating islands with flat ground and one block steps (most of the surface is flat), some terraced cliffs, a top
//   voxel that is thin at the top of a step and full at its foot so the steps look smooth;
// - an underside at least 12 blocks deep at the coast and up to about 160 inland (deep quarries do not dig through),
//   with rock spikes hanging under it;
// - ground types: grass (2, 7, 9) on flat ground, rock / forest floor (4) on slopes, coasts and under trees, snow (3)
//   up high;
// - forests of trees two cells apart, plants on the grass, boulders, iron and pyrite on the rock;
// - the markers the gamemodes need: player starting points, neutral crystals and corruptron spawn points;
// - hidden routes joining the markers and the islands, with steps of one block at most (WorldRoutes): the ground on
//   them looks like the ground round them, and forests on them are only thinned.
// Pure arithmetic on arrays, so it runs off the main thread.
internal static class WorldGenerator
{
	private const int N = GeneratedWorld.Size;

	private const int Margin = 8;

	// A route through a forest is a walkable lane: no trees within RouteClear columns of it (a lane of 3 + 2 * 2), and
	// a thinned edge out to RouteEdge (half the trees, fading), so it reads as a clearing, not a cut corridor.
	private const int RouteClear = 2;

	private const int RouteEdge = 4;

	private const byte Rock = 4;

	private const byte Snow = 3;

	private const byte GrassA = 2;

	private const byte GrassB = 7;

	private const byte GrassC = 9;

	// Radius of the land round a starting point, with its own forest, plants, boulders and ore.
	internal const int StartZone = 25;

	internal const string StartGroup = "Player Starting Point";

	internal const string CrystalGroup = "Crystal";

	internal const string SpawnGroup = "Corruptron Spawn Point";

	private static readonly int[] DX = { 1, -1, 0, 0 };

	private static readonly int[] DZ = { 0, 0, 1, -1 };

	internal static GeneratedWorld Generate(WorldSettings settings)
	{
		GeneratedWorld world = new GeneratedWorld { Settings = settings };
		Random random = new Random(settings.SeedValue);
		bool[] land = new bool[N * N];
		float[] height = new float[N * N];
		Shape(settings, random, land, height);
		RemoveSmallIslands(land, 250);
		int[] distance = EdgeDistance(land);
		Tops(world, land, height);
		int[] island = Islands(world, out int[] islandArea);
		bool[] reserved = new bool[N * N];
		bool[] taken = new bool[N * N];
		Markers(world, random, distance, island, islandArea, reserved);
		WorldRoutes.Build(world, distance, island, islandArea);
		// The routes level the ground and bridge the sky, so the coasts and the markers' heights are found again.
		for (int i = 0; i < N * N; i++)
		{
			land[i] = world.Top[i] != 0;
			taken[i] = world.Route[i];
		}
		distance = EdgeDistance(land);
		for (int m = 0; m < world.Markers.Count; m++)
		{
			GeneratedWorld.Marker marker = world.Markers[m];
			marker.Y = world.Surface(marker.X, marker.Z);
			world.Markers[m] = marker;
		}
		Undersides(world, random, distance);
		Forests(world, random, distance, reserved, taken);
		Ground(world, random);
		Boulders(world, random, reserved, taken);
		Minerals(world, random, distance, reserved, taken, world.Iron);
		Minerals(world, random, distance, reserved, taken, world.Pyrite);
		StartResources(world, random, distance, reserved, taken);
		Plants(world, random, reserved, taken);
		return world;
	}

	private static float Radius(WorldSize size)
	{
		switch (size)
		{
			case WorldSize.Small:
				return 210f;
			case WorldSize.Large:
				return 430f;
			default:
				return 310f;
		}
	}

	// Where the land is (a noise field cut off towards the edge of the map, warped so it is no circle) and the height
	// of its surface.
	private static void Shape(WorldSettings settings, Random random, bool[] land, float[] height)
	{
		WorldNoise warpX = new WorldNoise(random.Next());
		WorldNoise warpZ = new WorldNoise(random.Next());
		WorldNoise landNoise = new WorldNoise(random.Next());
		WorldNoise rifts = new WorldNoise(random.Next());
		WorldNoise regions = new WorldNoise(random.Next());
		WorldNoise hills = new WorldNoise(random.Next());
		WorldNoise ridges = new WorldNoise(random.Next());
		WorldNoise terraces = new WorldNoise(random.Next());
		float radius = Radius(settings.Size);
		float scale = radius / 310f;
		for (int z = Margin; z < N - Margin; z++)
		{
			for (int x = Margin; x < N - Margin; x++)
			{
				float dx = x - N / 2 + warpX.Fbm(x / 230f, z / 230f, 3) * radius * 0.35f;
				float dz = z - N / 2 + warpZ.Fbm(x / 230f, z / 230f, 3) * radius * 0.35f;
				float d = (float)Math.Sqrt(dx * dx + dz * dz) / radius;
				// Every style has islands: land cut by channels of sky (narrow enough for the routes' necks of land).
				// An archipelago is cut all over; highlands a little less, with mountains on top; a continent is one big
				// mainland with islands only round its edge.
				float score;
				float rift = Math.Abs(rifts.Fbm(x / (170f * scale), z / (170f * scale), 2));
				float riftWidth;
				switch (settings.Style)
				{
					case WorldStyle.Archipelago:
						score = landNoise.Fbm(x / (150f * scale), z / (150f * scale), 4) + 0.16f - Math.Max(0f, d - 0.55f) * 2.6f;
						riftWidth = 0.06f;
						break;
					case WorldStyle.Highlands:
						score = landNoise.Fbm(x / (170f * scale), z / (170f * scale), 4) + 0.22f - Math.Max(0f, d - 0.55f) * 2.6f;
						riftWidth = 0.045f;
						break;
					default:
						score = 0.8f - d + landNoise.Fbm(x / (200f * scale), z / (200f * scale), 4) * 0.45f;
						// Only the outer part of the continent breaks up into islands.
						riftWidth = 0.055f * Clamp01((d - 0.55f) * 4f);
						break;
				}
				if (rift < riftWidth)
				{
					score -= (riftWidth - rift) * 20f;
				}
				int i = x + z * N;
				land[i] = score > 0f;
				if (!land[i])
				{
					continue;
				}
				float region = regions.Fbm(x / 380f, z / 380f, 2);
				float h;
				switch (settings.Style)
				{
					case WorldStyle.Archipelago:
						h = 150f + region * 32f + hills.Fbm(x / 110f, z / 110f, 4) * 11f;
						break;
					case WorldStyle.Highlands:
						h = 125f + region * 18f + hills.Fbm(x / 110f, z / 110f, 4) * 14f;
						// Mountain ranges, rising away from the coast to snowy peaks.
						float ridge = ridges.Ridged(x / 200f, z / 200f, 5);
						h += (float)Math.Pow(ridge, 1.6) * 115f * Clamp01(score * 2f);
						break;
					default:
						h = 150f + region * 14f + hills.Fbm(x / 120f, z / 120f, 4) * 15f;
						break;
				}
				// Terraces in some places: flat shelves with a cliff between them.
				float terrace = Clamp01(terraces.Fbm(x / 300f, z / 300f, 2) * 3f);
				if (terrace > 0f)
				{
					float step = (settings.Style == WorldStyle.Highlands) ? 7f : 5f;
					float t = h / step;
					float floor = (float)Math.Floor(t);
					float stepped = step * (floor + SmoothStep(0.42f, 0.58f, t - floor));
					h += (stepped - h) * terrace;
				}
				height[i] = h;
			}
		}
	}

	private static void RemoveSmallIslands(bool[] land, int minimum)
	{
		bool[] seen = new bool[N * N];
		List<int> cells = new List<int>();
		Queue<int> open = new Queue<int>();
		for (int start = 0; start < N * N; start++)
		{
			if (!land[start] || seen[start])
			{
				continue;
			}
			cells.Clear();
			seen[start] = true;
			open.Enqueue(start);
			while (open.Count > 0)
			{
				int i = open.Dequeue();
				cells.Add(i);
				int x = i % N;
				int z = i / N;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + DX[k];
					int nz = z + DZ[k];
					if (nx < 0 || nz < 0 || nx >= N || nz >= N)
					{
						continue;
					}
					int j = nx + nz * N;
					if (land[j] && !seen[j])
					{
						seen[j] = true;
						open.Enqueue(j);
					}
				}
			}
			if (cells.Count < minimum)
			{
				foreach (int i in cells)
				{
					land[i] = false;
				}
			}
		}
	}

	// Blocks from each land cell to the nearest sky (chamfer distance, about Euclidean).
	internal static int[] EdgeDistance(bool[] land)
	{
		const int Straight = 3;
		const int Diagonal = 4;
		int[] d = new int[N * N];
		for (int i = 0; i < N * N; i++)
		{
			d[i] = land[i] ? int.MaxValue / 2 : 0;
		}
		for (int z = 1; z < N - 1; z++)
		{
			for (int x = 1; x < N - 1; x++)
			{
				int i = x + z * N;
				if (d[i] == 0)
				{
					continue;
				}
				d[i] = Math.Min(d[i], Math.Min(d[i - 1] + Straight, d[i - N] + Straight));
				d[i] = Math.Min(d[i], Math.Min(d[i - N - 1] + Diagonal, d[i - N + 1] + Diagonal));
			}
		}
		for (int z = N - 2; z >= 1; z--)
		{
			for (int x = N - 2; x >= 1; x--)
			{
				int i = x + z * N;
				if (d[i] == 0)
				{
					continue;
				}
				d[i] = Math.Min(d[i], Math.Min(d[i + 1] + Straight, d[i + N] + Straight));
				d[i] = Math.Min(d[i], Math.Min(d[i + N + 1] + Diagonal, d[i + N - 1] + Diagonal));
			}
		}
		for (int i = 0; i < N * N; i++)
		{
			d[i] /= Straight;
		}
		return d;
	}

	private static void Tops(GeneratedWorld world, bool[] land, float[] height)
	{
		for (int i = 0; i < N * N; i++)
		{
			if (land[i])
			{
				world.Top[i] = (byte)Clamp((int)Math.Round(height[i]), 40, 245);
			}
		}
	}

	// Bottom and the top voxel's fill of every column.
	private static void Undersides(GeneratedWorld world, Random random, int[] distance)
	{
		WorldNoise under = new WorldNoise(random.Next());
		WorldNoise spikes = new WorldNoise(random.Next());
		for (int z = 0; z < N; z++)
		{
			for (int x = 0; x < N; x++)
			{
				int i = x + z * N;
				int top = world.Top[i];
				if (top == 0)
				{
					continue;
				}
				float dist = distance[i];
				float u = under.Fbm(x / 45f, z / 45f, 3);
				float spike = Math.Max(0f, spikes.Fbm(x / 16f, z / 16f, 2));
				float thickness = 12f + dist * (1.8f + 0.4f * u) + spike * spike * 60f * Math.Min(1f, dist / 12f);
				thickness = Math.Min(thickness, 155f + 20f * u);
				int bottom = Math.Max(1, top - Math.Max(4, (int)thickness));
				world.Bottom[i] = (byte)bottom;
				world.Fill[i] = 254;
				bool higher = false;
				bool lower = false;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + DX[k];
					int nz = z + DZ[k];
					if (!world.Solid(nx, nz))
					{
						continue;
					}
					int other = world.Top[nx + nz * N];
					higher |= other == top + 1;
					lower |= other == top - 1;
				}
				if (higher && !lower)
				{
					world.Fill[i] = 253;
				}
				else if (lower && !higher)
				{
					world.Fill[i] = 255;
				}
			}
		}
	}

	// Which island each column belongs to (-1 for sky), and each island's size.
	private static int[] Islands(GeneratedWorld world, out int[] area)
	{
		int[] island = new int[N * N];
		for (int i = 0; i < island.Length; i++)
		{
			island[i] = -1;
		}
		List<int> areas = new List<int>();
		Queue<int> open = new Queue<int>();
		for (int start = 0; start < N * N; start++)
		{
			if (world.Top[start] == 0 || island[start] >= 0)
			{
				continue;
			}
			int id = areas.Count;
			int count = 0;
			island[start] = id;
			open.Enqueue(start);
			while (open.Count > 0)
			{
				int i = open.Dequeue();
				count++;
				int x = i % N;
				int z = i / N;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + DX[k];
					int nz = z + DZ[k];
					if (!world.Solid(nx, nz))
					{
						continue;
					}
					int j = nx + nz * N;
					if (island[j] < 0)
					{
						island[j] = id;
						open.Enqueue(j);
					}
				}
			}
			areas.Add(count);
		}
		area = areas.ToArray();
		return island;
	}

	// The most a column's top differs from its neighbours'; a coast counts as a big step.
	private static int Slope(GeneratedWorld world, int x, int z)
	{
		int top = world.Top[x + z * N];
		int slope = 0;
		for (int k = 0; k < 4; k++)
		{
			int nx = x + DX[k];
			int nz = z + DZ[k];
			if (!world.Solid(nx, nz))
			{
				return 99;
			}
			slope = Math.Max(slope, Math.Abs(world.Top[nx + nz * N] - top));
		}
		return slope;
	}

	// All columns within the square of the given radius are land at most `step` from the middle's height.
	private static bool Flat(GeneratedWorld world, int x, int z, int radius, int step)
	{
		int top = world.Top[x + z * N];
		for (int dz = -radius; dz <= radius; dz++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				if (!world.Solid(x + dx, z + dz) || Math.Abs(world.Top[x + dx + (z + dz) * N] - top) > step)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static void Markers(GeneratedWorld world, Random random, int[] distance, int[] island, int[] islandArea, bool[] reserved)
	{
		WorldSize size = world.Settings.Size;
		int starts = (size == WorldSize.Small) ? 2 : ((size == WorldSize.Large) ? 6 : 4);
		int crystals = (size == WorldSize.Small) ? 6 : ((size == WorldSize.Large) ? 12 : 9);
		int spawns = (size == WorldSize.Small) ? 6 : ((size == WorldSize.Large) ? 14 : 10);
		int startSpacing = (size == WorldSize.Small) ? 90 : 130;
		List<int> picked = new List<int>();

		// Starting points and corruptron spawn points only on the main island: the smaller islands hold crystals,
		// forests and ore to reach for, but nobody starts or spawns there.
		int main = 0;
		for (int k = 1; k < islandArea.Length; k++)
		{
			if (islandArea[k] > islandArea[main])
			{
				main = k;
			}
		}

		// Starting points: big flat places well inland (a start zone of StartZone blocks round them is land), as far
		// apart as possible.
		for (int relax = 0; relax < 4 && Picked(world, StartGroup) == 0; relax++)
		{
			int inland = StartZone - relax * 5;
			int radius = 5 - relax;
			List<int> candidates = Candidates(world, 3, (int i, int x, int z) => distance[i] >= inland && island[i] == main && Flat(world, x, z, radius, (relax < 2) ? 1 : 2));
			Pick(world, random, candidates, starts, startSpacing / (relax + 1), null, 0, StartGroup, picked);
		}
		List<int> startCells = new List<int>(picked);
		FillStartZones(world, startCells, island, main);
		// Only the middle is kept free (the crystal and the first bricktrons); the rest of the zone gets its forest,
		// plants, boulders and ore (StartResources).
		Reserve(reserved, startCells, 6);

		// Neutral crystals: flat places away from the starting points.
		picked.Clear();
		List<int> crystalCells = Candidates(world, 3, (int i, int x, int z) => distance[i] >= 6 && !reserved[i] && Flat(world, x, z, 2, 1));
		Pick(world, random, crystalCells, crystals, 60, startCells, 55, CrystalGroup, picked);
		Reserve(reserved, picked, 6);

		// Corruptron spawn points: near the coasts, far from the starting points.
		picked.Clear();
		int far = (size == WorldSize.Small) ? 80 : 110;
		List<int> spawnCells = Candidates(world, 2, (int i, int x, int z) => distance[i] >= 3 && distance[i] <= 10 && island[i] == main && !reserved[i] && Flat(world, x, z, 1, 1));
		Pick(world, random, spawnCells, spawns, 45, startCells, far, SpawnGroup, picked);
		if (Picked(world, SpawnGroup) == 0)
		{
			Pick(world, random, spawnCells, spawns, 30, startCells, far / 2, SpawnGroup, picked);
		}
		Reserve(reserved, picked, 5);
	}

	private delegate bool CellTest(int i, int x, int z);

	private static List<int> Candidates(GeneratedWorld world, int every, CellTest test)
	{
		List<int> cells = new List<int>();
		for (int z = Margin; z < N - Margin; z += every)
		{
			for (int x = Margin; x < N - Margin; x += every)
			{
				int i = x + z * N;
				if (world.Top[i] != 0 && test(i, x, z))
				{
					cells.Add(i);
				}
			}
		}
		return cells;
	}

	private static int Picked(GeneratedWorld world, string group)
	{
		return world.Count(group);
	}

	// Greedy farthest-point picking: each new marker is the candidate farthest from the markers of its group (and at
	// least `away` from the avoided cells), until `count` are placed or none is `spacing` away any more.
	private static void Pick(GeneratedWorld world, Random random, List<int> candidates, int count, int spacing, List<int> avoid, int away, string group, List<int> picked)
	{
		if (candidates.Count == 0)
		{
			return;
		}
		List<int> usable = new List<int>();
		foreach (int c in candidates)
		{
			if (avoid == null || MinDistance(c, avoid) >= away)
			{
				usable.Add(c);
			}
		}
		if (usable.Count == 0)
		{
			return;
		}
		int placed = picked.Count;
		picked.Add(usable[random.Next(usable.Count)]);
		AddMarker(world, picked[picked.Count - 1], group);
		while (picked.Count - placed < count)
		{
			int best = -1;
			int bestDistance = -1;
			foreach (int c in usable)
			{
				int d = MinDistance(c, picked);
				if (d > bestDistance)
				{
					bestDistance = d;
					best = c;
				}
			}
			if (best < 0 || bestDistance < spacing)
			{
				break;
			}
			picked.Add(best);
			AddMarker(world, best, group);
		}
	}

	private static int MinDistance(int cell, List<int> others)
	{
		int x = cell % N;
		int z = cell / N;
		int best = int.MaxValue;
		foreach (int o in others)
		{
			int dx = o % N - x;
			int dz = o / N - z;
			best = Math.Min(best, dx * dx + dz * dz);
		}
		return (best == int.MaxValue) ? int.MaxValue : (int)Math.Sqrt(best);
	}

	private static void AddMarker(GeneratedWorld world, int cell, string group)
	{
		int x = cell % N;
		int z = cell / N;
		world.Markers.Add(new GeneratedWorld.Marker { Group = group, X = x, Y = world.Surface(x, z), Z = z });
	}

	// Every start zone is land: holes in it (sky, a bay) are filled at the starting point's height.
	private static void FillStartZones(GeneratedWorld world, List<int> starts, int[] island, int main)
	{
		foreach (int cell in starts)
		{
			int cx = cell % N;
			int cz = cell / N;
			byte top = world.Top[cell];
			for (int dz = -StartZone; dz <= StartZone; dz++)
			{
				for (int dx = -StartZone; dx <= StartZone; dx++)
				{
					int x = cx + dx;
					int z = cz + dz;
					if (x < Margin || z < Margin || x >= N - Margin || z >= N - Margin || dx * dx + dz * dz > StartZone * StartZone)
					{
						continue;
					}
					int i = x + z * N;
					if (world.Top[i] == 0)
					{
						world.Top[i] = top;
						island[i] = main;
					}
				}
			}
		}
	}

	private static void Reserve(bool[] reserved, List<int> cells, int radius)
	{
		foreach (int cell in cells)
		{
			int cx = cell % N;
			int cz = cell / N;
			for (int dz = -radius; dz <= radius; dz++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					int x = cx + dx;
					int z = cz + dz;
					if (x >= 0 && z >= 0 && x < N && z < N && dx * dx + dz * dz <= radius * radius)
					{
						reserved[x + z * N] = true;
					}
				}
			}
		}
	}

	// Forests: where a noise is high, trees on every other cell (as on the game's maps), on rock floor.
	private static void Forests(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken)
	{
		WorldNoise forest = new WorldNoise(random.Next());
		float threshold;
		switch (world.Settings.Biome)
		{
			case WorldBiome.Desert:
				threshold = 0.3f;
				break;
			case WorldBiome.Winter:
				threshold = 0.08f;
				break;
			default:
				threshold = 0f;
				break;
		}
		if (world.Settings.Style == WorldStyle.Highlands)
		{
			threshold += 0.05f;
		}
		// Fewer trees: smaller and thinner forests; more: bigger and denser ones.
		float amount = WorldSettings.Multiplier(world.Settings.Trees);
		if (amount <= 0f)
		{
			return;
		}
		threshold += (1f - Math.Min(amount, 2f)) * 0.18f;
		int[] routeDistance = RouteDistance(world, RouteEdge + 1);
		for (int z = Margin; z < N - Margin; z++)
		{
			for (int x = Margin; x < N - Margin; x++)
			{
				int i = x + z * N;
				if (world.Top[i] == 0)
				{
					continue;
				}
				float f = forest.Fbm(x / 100f, z / 100f, 3);
				if (f < threshold || world.Top[i] > 215)
				{
					continue;
				}
				world.Forest[i] = true;
				if ((x & 1) != 0 || (z & 1) != 0 || reserved[i] || distance[i] < 2 || Slope(world, x, z) > 1)
				{
					continue;
				}
				float density = Math.Min(0.95f, ((f - threshold) * 3.5f + 0.15f) * Math.Max(1f, amount * 0.6f));
				// A lane along a route, its edge thinned.
				int fromRoute = routeDistance[i];
				if (fromRoute <= RouteClear)
				{
					continue;
				}
				if (fromRoute <= RouteEdge)
				{
					density *= 0.25f + 0.25f * (fromRoute - RouteClear);
				}
				if (random.NextDouble() >= density)
				{
					continue;
				}
				int grown = (random.NextDouble() < 0.85) ? 15 : 6 + random.Next(9);
				world.Trees.Add(new GeneratedWorld.Tree { X = x, Y = world.Surface(x, z), Z = z, Size = grown });
				taken[i] = true;
			}
		}
	}

	// Columns from the nearest route column (8-neighbour steps), up to limit; limit beyond that.
	private static int[] RouteDistance(GeneratedWorld world, int limit)
	{
		int[] result = new int[N * N];
		Queue<int> open = new Queue<int>();
		for (int i = 0; i < N * N; i++)
		{
			if (world.Route[i])
			{
				open.Enqueue(i);
			}
			else
			{
				result[i] = limit;
			}
		}
		while (open.Count > 0)
		{
			int i = open.Dequeue();
			int next = result[i] + 1;
			if (next >= limit)
			{
				continue;
			}
			int x = i % N;
			int z = i / N;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int nx = x + dx;
					int nz = z + dz;
					if (nx < 0 || nz < 0 || nx >= N || nz >= N)
					{
						continue;
					}
					int j = nx + nz * N;
					if (result[j] > next)
					{
						result[j] = next;
						open.Enqueue(j);
					}
				}
			}
		}
		return result;
	}

	// Ground types: rock on slopes, coasts and forest floor, snow up high, patches of the three grasses elsewhere.
	private static void Ground(GeneratedWorld world, Random random)
	{
		WorldNoise patches = new WorldNoise(random.Next());
		WorldNoise snow = new WorldNoise(random.Next());
		for (int z = 0; z < N; z++)
		{
			for (int x = 0; x < N; x++)
			{
				int i = x + z * N;
				int top = world.Top[i];
				if (top == 0)
				{
					continue;
				}
				byte type;
				if (world.Forest[i] || Slope(world, x, z) >= 2)
				{
					type = Rock;
				}
				else
				{
					type = (patches.Fbm(x / 40f, z / 40f, 3) < 0.05f) ? GrassC : GrassB;
				}
				bool snowy = top > 205 + (int)(snow.Sample(x / 30f, z / 30f) * 6f);
				if (world.Settings.Biome == WorldBiome.Winter && type != Rock)
				{
					snowy |= snow.Fbm(x / 70f, z / 70f, 3) > 0.05f;
				}
				if (snowy && Slope(world, x, z) < 2)
				{
					type = Snow;
				}
				world.Crust[i] = type;
			}
		}
	}

	private static void Boulders(GeneratedWorld world, Random random, bool[] reserved, bool[] taken)
	{
		int clusters = (int)(((world.Settings.Size == WorldSize.Small) ? 25 : ((world.Settings.Size == WorldSize.Large) ? 70 : 45)) * WorldSettings.Multiplier(world.Settings.Boulders));
		for (int c = 0, tries = 0; c < clusters && tries < clusters * 50; tries++)
		{
			int cx = Margin + random.Next(N - 2 * Margin);
			int cz = Margin + random.Next(N - 2 * Margin);
			if (!world.Solid(cx, cz) || reserved[cx + cz * N])
			{
				continue;
			}
			c++;
			int boulders = 1 + random.Next(4);
			for (int b = 0; b < boulders; b++)
			{
				int x = cx + random.Next(-6, 7);
				int z = cz + random.Next(-6, 7);
				bool big = random.NextDouble() < 0.3;
				// A 3x3 boulder covers x-1..x+1, a 2x2 one x..x+1.
				int from = big ? -1 : 0;
				if (!FreeSquare(world, x, z, from, 1, reserved, taken))
				{
					continue;
				}
				for (int dz = from; dz <= 1; dz++)
				{
					for (int dx = from; dx <= 1; dx++)
					{
						taken[x + dx + (z + dz) * N] = true;
					}
				}
				float half = big ? 0f : 0.5f;
				world.Boulders.Add(new GeneratedWorld.Boulder
				{
					X = x + half,
					Y = world.Surface(x, z),
					Z = z + half,
					OffsetX = (float)(random.NextDouble() * 0.6 - 0.3),
					OffsetZ = (float)(random.NextDouble() * 0.6 - 0.3),
					Growth = (float)(0.5 + random.NextDouble() * 0.5),
					Cells = big ? 9 : 4
				});
			}
		}
	}

	// The cells from..to around (x, z) are free, flat land.
	private static bool FreeSquare(GeneratedWorld world, int x, int z, int from, int to, bool[] reserved, bool[] taken)
	{
		if (!world.Solid(x, z))
		{
			return false;
		}
		int top = world.Top[x + z * N];
		for (int dz = from; dz <= to; dz++)
		{
			for (int dx = from; dx <= to; dx++)
			{
				int nx = x + dx;
				int nz = z + dz;
				if (!world.Solid(nx, nz))
				{
					return false;
				}
				int j = nx + nz * N;
				if (reserved[j] || taken[j] || world.Top[j] != top)
				{
					return false;
				}
			}
		}
		return true;
	}

	// Clusters of iron or pyrite on rock: a short walk of cells from a slope or forest floor.
	private static void Minerals(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken, List<GeneratedWorld.Mineral> into)
	{
		int clusters = (int)(((world.Settings.Size == WorldSize.Small) ? 12 : ((world.Settings.Size == WorldSize.Large) ? 32 : 20)) * WorldSettings.Multiplier(world.Settings.SurfaceOre));
		for (int c = 0, tries = 0; c < clusters && tries < clusters * 200; tries++)
		{
			int x = Margin + random.Next(N - 2 * Margin);
			int z = Margin + random.Next(N - 2 * Margin);
			int i = x + z * N;
			if (!world.Solid(x, z) || reserved[i] || taken[i] || world.Crust[i] != Rock || distance[i] < 2)
			{
				continue;
			}
			c++;
			int cells = 4 + random.Next(7);
			for (int k = 0; k < cells * 3 && cells > 0; k++)
			{
				int j = x + z * N;
				if (!taken[j] && !reserved[j])
				{
					taken[j] = true;
					world.Crust[j] = Rock;
					into.Add(new GeneratedWorld.Mineral { X = x, Y = world.Surface(x, z), Z = z, Growth = (float)Math.Round(0.2 + random.NextDouble() * 0.8, 2) });
					cells--;
				}
				int dir = random.Next(4);
				int nx = x + DX[dir];
				int nz = z + DZ[dir];
				if (world.Solid(nx, nz) && distance[nx + nz * N] >= 2 && Math.Abs(world.Top[nx + nz * N] - world.Top[x + z * N]) <= 1)
				{
					x = nx;
					z = nz;
				}
			}
		}
	}

	// Every start zone gets, whatever the settings, a grove of trees (when its forest has few), two clusters of
	// boulders, and ore 9 to 22 blocks from the starting point: a cluster of iron
	// deposits, one of pyrite deposits (orange crystal) and one of coal deposits (with Metallurgy on: iron deposits
	// marked by their growth, CoalDeposits).
	private static void StartResources(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken)
	{
		bool coal = Plugin.IsEnabled(Features.Metallurgy);
		foreach (GeneratedWorld.Marker marker in world.Markers)
		{
			if (marker.Group != StartGroup)
			{
				continue;
			}
			if (TreesNear(world, marker) < 12 && Near(world, random, distance, reserved, taken, marker, out int gx, out int gz))
			{
				Grove(world, random, distance, reserved, taken, gx, gz, 12 + random.Next(8));
			}
			for (int k = 0; k < 2; k++)
			{
				if (Near(world, random, distance, reserved, taken, marker, out int bx, out int bz))
				{
					BoulderCluster(world, random, reserved, taken, bx, bz, 2 + random.Next(3));
				}
			}
			if (Near(world, random, distance, reserved, taken, marker, out int ix, out int iz))
			{
				Deposit(world, random, distance, reserved, taken, ix, iz, 6 + random.Next(4), world.Iron);
			}
			if (Near(world, random, distance, reserved, taken, marker, out int ox, out int oz))
			{
				Deposit(world, random, distance, reserved, taken, ox, oz, 6 + random.Next(4), world.Pyrite);
			}
			if (coal && Near(world, random, distance, reserved, taken, marker, out int cx, out int cz))
			{
				Deposit(world, random, distance, reserved, taken, cx, cz, 6 + random.Next(4), world.Iron, Metallurgy.CoalDeposits.CoalGrowth);
			}
		}
	}

	// A free, flat place 9 to 22 blocks from the marker (in its start zone).
	private static bool Near(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken, GeneratedWorld.Marker marker, out int x, out int z)
	{
		for (int tries = 0; tries < 200; tries++)
		{
			double angle = random.NextDouble() * Math.PI * 2.0;
			double far = 9 + random.NextDouble() * 13;
			x = marker.X + (int)Math.Round(Math.Cos(angle) * far);
			z = marker.Z + (int)Math.Round(Math.Sin(angle) * far);
			if (x < Margin || z < Margin || x >= N - Margin || z >= N - Margin)
			{
				continue;
			}
			int i = x + z * N;
			if (world.Solid(x, z) && !reserved[i] && !taken[i] && distance[i] >= 3 && Slope(world, x, z) <= 1)
			{
				return true;
			}
		}
		x = 0;
		z = 0;
		return false;
	}

	// Iron deposits (iron.json) or pyrite deposits (pyrite.json, which give orange crystal), as Minerals lays them;
	// a fixed growth instead of a random one when given.
	private static void Deposit(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken, int x, int z, int cells, List<GeneratedWorld.Mineral> into, float growth = -1f)
	{
		for (int k = 0; k < cells * 4 && cells > 0; k++)
		{
			int j = x + z * N;
			if (!taken[j] && !reserved[j])
			{
				taken[j] = true;
				world.Crust[j] = Rock;
				float grown = (growth >= 0f) ? growth : (float)Math.Round(0.6 + random.NextDouble() * 0.4, 2);
				into.Add(new GeneratedWorld.Mineral { X = x, Y = world.Surface(x, z), Z = z, Growth = grown });
				cells--;
			}
			Wander(world, random, distance, ref x, ref z);
		}
	}

	// A route column within reach of (x, z): the forests keep a lane along the routes clear.
	private static bool NearRoute(GeneratedWorld world, int x, int z, int reach)
	{
		for (int dz = -reach; dz <= reach; dz++)
		{
			for (int dx = -reach; dx <= reach; dx++)
			{
				int nx = x + dx;
				int nz = z + dz;
				if (nx >= 0 && nz >= 0 && nx < N && nz < N && world.Route[nx + nz * N])
				{
					return true;
				}
			}
		}
		return false;
	}

	private static int TreesNear(GeneratedWorld world, GeneratedWorld.Marker marker)
	{
		int count = 0;
		foreach (GeneratedWorld.Tree tree in world.Trees)
		{
			int dx = tree.X - marker.X;
			int dz = tree.Z - marker.Z;
			if (dx * dx + dz * dz <= StartZone * StartZone)
			{
				count++;
			}
		}
		return count;
	}

	// Trees two cells apart round (x, z), as a forest grows them.
	private static void Grove(GeneratedWorld world, Random random, int[] distance, bool[] reserved, bool[] taken, int x, int z, int count)
	{
		for (int tries = 0; tries < count * 12 && count > 0; tries++)
		{
			int tx = ((x + random.Next(-7, 8)) / 2) * 2;
			int tz = ((z + random.Next(-7, 8)) / 2) * 2;
			if (!world.Solid(tx, tz))
			{
				continue;
			}
			int i = tx + tz * N;
			if (reserved[i] || taken[i] || distance[i] < 2 || Slope(world, tx, tz) > 1 || NearRoute(world, tx, tz, RouteClear))
			{
				continue;
			}
			taken[i] = true;
			world.Forest[i] = true;
			int grown = (random.NextDouble() < 0.85) ? 15 : 6 + random.Next(9);
			world.Trees.Add(new GeneratedWorld.Tree { X = tx, Y = world.Surface(tx, tz), Z = tz, Size = grown });
			count--;
		}
	}

	// A few boulders round (x, z), as Boulders lays them.
	private static void BoulderCluster(GeneratedWorld world, Random random, bool[] reserved, bool[] taken, int cx, int cz, int boulders)
	{
		for (int tries = 0; tries < boulders * 8 && boulders > 0; tries++)
		{
			int x = cx + random.Next(-4, 5);
			int z = cz + random.Next(-4, 5);
			bool big = random.NextDouble() < 0.3;
			int from = big ? -1 : 0;
			if (!FreeSquare(world, x, z, from, 1, reserved, taken))
			{
				continue;
			}
			for (int dz = from; dz <= 1; dz++)
			{
				for (int dx = from; dx <= 1; dx++)
				{
					taken[x + dx + (z + dz) * N] = true;
				}
			}
			float half = big ? 0f : 0.5f;
			world.Boulders.Add(new GeneratedWorld.Boulder
			{
				X = x + half,
				Y = world.Surface(x, z),
				Z = z + half,
				OffsetX = (float)(random.NextDouble() * 0.6 - 0.3),
				OffsetZ = (float)(random.NextDouble() * 0.6 - 0.3),
				Growth = (float)(0.5 + random.NextDouble() * 0.5),
				Cells = big ? 9 : 4
			});
			boulders--;
		}
	}

	private static void Wander(GeneratedWorld world, Random random, int[] distance, ref int x, ref int z)
	{
		int dir = random.Next(4);
		int nx = x + DX[dir];
		int nz = z + DZ[dir];
		if (world.Solid(nx, nz) && distance[nx + nz * N] >= 2 && Math.Abs(world.Top[nx + nz * N] - world.Top[x + z * N]) <= 1)
		{
			x = nx;
			z = nz;
		}
	}

	private static void Plants(GeneratedWorld world, Random random, bool[] reserved, bool[] taken)
	{
		WorldNoise meadows = new WorldNoise(random.Next());
		float rate;
		int[] weights;
		switch (world.Settings.Biome)
		{
			case WorldBiome.Winter:
				rate = 0.04f;
				weights = new[] { 35, 15, 12, 20, 9, 9 };
				break;
			case WorldBiome.Desert:
				rate = 0.025f;
				weights = new[] { 10, 8, 10, 35, 12, 25 };
				break;
			default:
				rate = 0.07f;
				weights = new[] { 60, 15, 5, 3, 15, 2 };
				break;
		}
		int total = 0;
		foreach (int w in weights)
		{
			total += w;
		}
		for (int z = Margin; z < N - Margin; z++)
		{
			for (int x = Margin; x < N - Margin; x++)
			{
				int i = x + z * N;
				byte type = world.Crust[i];
				if (world.Top[i] == 0 || taken[i] || (type != GrassA && type != GrassB && type != GrassC && type != Snow))
				{
					continue;
				}
				float chance = rate * Clamp01(0.5f + meadows.Fbm(x / 60f, z / 60f, 2) * 1.2f);
				if (random.NextDouble() >= chance)
				{
					continue;
				}
				// Near a starting point only a few, so the first buildings have room.
				if (reserved[i] && random.NextDouble() < 0.8)
				{
					continue;
				}
				int roll = random.Next(total);
				int kind = 0;
				while (roll >= weights[kind])
				{
					roll -= weights[kind];
					kind++;
				}
				world.Plants.Add(new GeneratedWorld.Plant { X = x, Y = world.Surface(x, z), Z = z, Growth = (float)Math.Round(0.05 + random.NextDouble() * 0.95, 3), Type = kind });
				taken[i] = true;
			}
		}
	}

	private static float Clamp01(float v)
	{
		return (v < 0f) ? 0f : ((v > 1f) ? 1f : v);
	}

	private static int Clamp(int v, int min, int max)
	{
		return (v < min) ? min : ((v > max) ? max : v);
	}

	private static float SmoothStep(float from, float to, float t)
	{
		float x = Clamp01((t - from) / (to - from));
		return x * x * (3f - 2f * x);
	}
}
