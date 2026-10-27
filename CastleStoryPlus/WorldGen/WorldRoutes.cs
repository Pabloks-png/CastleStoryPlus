using System;
using System.Collections.Generic;

namespace CastleStoryPlus.WorldGen;

// Hidden routes that join a random world up, so bricktrons (and enemies) can walk around it:
// - the places to join are the markers (starting points, crystals, spawn points) and the middle of every island big
//   enough to matter that has none;
// - they are joined closest pairs first (Kruskal: every place reachable where it can be, few routes), plus a route from
//   each starting point to the nearest other one;
// - each route is the cheapest walk on the grid (A*, eight directions, a noise adds a little cost so it winds): over
//   land, avoiding big steps, or over a narrow gap of sky at a high cost (a short land bridge; wide gaps are not
//   bridged);
// - along the walk the ground is levelled to steps of one block at most (smoothed, then clamped from the far end) over
//   a width of three columns, without boulders or minerals, through forests a lane 7 columns wide cleared of trees
//   (its edge thinned, WorldGenerator.Forests); the ground beside it bends
//   smoothly towards it, and over the sky a neck of land 8 to 16 columns wide (its edge wanders) carries it, so islands
//   join like a natural isthmus instead of a bridge.
internal static class WorldRoutes
{
	private const int N = GeneratedWorld.Size;

	private const int Margin = 10;

	// Islands smaller than this get no route of their own.
	private const int IslandArea = 1500;

	private const int LandCost = 10;

	private const int StepCost = 30;

	private const int SkyCost = 150;

	private const int SmoothRadius = 6;

	// How far beside a route the ground bends towards it.
	private const int BlendRadius = 7;

	// Half width of the neck of land that carries a route over the sky.
	private const float NeckMin = 4f;

	private const float NeckMax = 8f;

	// Sky columns farther than this from land cannot be crossed, so a bridge is at most about twice as long.
	private const int MaxBridgeHalf = 8;

	// Routes searched at most to join the places up.
	private const int MaxTries = 80;

	// Straight steps first, then the diagonal ones.
	private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };

	private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

	// Up to this much extra cost per step from a noise, so a route winds a little instead of running straight.
	private const int Wander = 8;

	internal static void Build(GeneratedWorld world, int[] distance, int[] island, int[] islandArea)
	{
		List<int> places = Places(world, distance, island, islandArea);
		if (places.Count < 2)
		{
			return;
		}
		bool[] sky = new bool[N * N];
		for (int i = 0; i < N * N; i++)
		{
			sky[i] = world.Top[i] == 0;
		}
		// How far each sky column is from land: a bridge may only cross a gap up to twice MaxBridgeHalf wide.
		int[] shore = WorldGenerator.EdgeDistance(sky);
		Search search = new Search(new WorldNoise(world.Settings.SeedValue ^ 0x5EED), shore);
		int[] reach = Reach(world, shore);
		WorldNoise widths = new WorldNoise(world.Settings.SeedValue ^ 0x4EC4);
		int[] group = new int[places.Count];
		for (int i = 0; i < group.Length; i++)
		{
			group[i] = i;
		}
		// Kruskal: the closest pairs first, each joined when it is not yet reachable and a route can be laid (a gap too
		// wide to bridge makes the next pair be tried).
		int tries = 0;
		int groups = places.Count;
		foreach (KeyValuePair<int, int> pair in Pairs(places))
		{
			if (groups == 1 || tries >= MaxTries)
			{
				break;
			}
			int a = Root(group, pair.Key);
			int b = Root(group, pair.Value);
			if (a == b || reach[places[pair.Key]] != reach[places[pair.Value]])
			{
				continue;
			}
			tries++;
			if (Join(world, search, widths, places[pair.Key], places[pair.Value]))
			{
				group[a] = b;
				groups--;
			}
		}
		// A second way between neighbouring starting points.
		List<int> starts = new List<int>();
		for (int i = 0; i < world.Markers.Count && i < places.Count; i++)
		{
			if (world.Markers[i].Group == WorldGenerator.StartGroup)
			{
				starts.Add(i);
			}
		}
		foreach (int a in starts)
		{
			int nearest = -1;
			foreach (int b in starts)
			{
				if (b != a && (nearest < 0 || Distance2(places[a], places[b]) < Distance2(places[a], places[nearest])))
				{
					nearest = b;
				}
			}
			if (nearest >= 0)
			{
				Join(world, search, widths, places[a], places[nearest]);
			}
		}
	}

	// Which places a route could ever join: land and narrow sky flooded together, one number per connected part.
	private static int[] Reach(GeneratedWorld world, int[] shore)
	{
		int[] part = new int[N * N];
		int next = 0;
		Queue<int> open = new Queue<int>();
		for (int start = 0; start < N * N; start++)
		{
			if (part[start] != 0 || world.Top[start] == 0)
			{
				continue;
			}
			next++;
			part[start] = next;
			open.Enqueue(start);
			while (open.Count > 0)
			{
				int i = open.Dequeue();
				int x = i % N;
				int z = i / N;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + DX[k];
					int nz = z + DZ[k];
					if (nx < Margin || nz < Margin || nx >= N - Margin || nz >= N - Margin)
					{
						continue;
					}
					int j = nx + nz * N;
					if (part[j] == 0 && (world.Top[j] != 0 || shore[j] <= MaxBridgeHalf))
					{
						part[j] = next;
						open.Enqueue(j);
					}
				}
			}
		}
		return part;
	}

	private static bool Join(GeneratedWorld world, Search search, WorldNoise widths, int from, int to)
	{
		List<int> path = search.Find(world, from, to);
		if (path == null || path.Count < 2)
		{
			return false;
		}
		Lay(world, path, widths);
		return true;
	}

	private static int Root(int[] group, int i)
	{
		while (group[i] != i)
		{
			group[i] = group[group[i]];
			i = group[i];
		}
		return i;
	}

	// Every pair of places, closest first.
	private static List<KeyValuePair<int, int>> Pairs(List<int> places)
	{
		List<KeyValuePair<int, int>> pairs = new List<KeyValuePair<int, int>>();
		for (int a = 0; a < places.Count; a++)
		{
			for (int b = a + 1; b < places.Count; b++)
			{
				pairs.Add(new KeyValuePair<int, int>(a, b));
			}
		}
		pairs.Sort((KeyValuePair<int, int> p, KeyValuePair<int, int> q) => Distance2(places[p.Key], places[p.Value]).CompareTo(Distance2(places[q.Key], places[q.Value])));
		return pairs;
	}

	private static List<int> Places(GeneratedWorld world, int[] distance, int[] island, int[] islandArea)
	{
		List<int> places = new List<int>();
		bool[] hasMarker = new bool[islandArea.Length];
		foreach (GeneratedWorld.Marker marker in world.Markers)
		{
			int i = marker.X + marker.Z * N;
			places.Add(i);
			if (island[i] >= 0)
			{
				hasMarker[island[i]] = true;
			}
		}
		// The most inland column of each big island without a marker.
		int[] middle = new int[islandArea.Length];
		for (int k = 0; k < middle.Length; k++)
		{
			middle[k] = -1;
		}
		for (int i = 0; i < N * N; i++)
		{
			int k = island[i];
			if (k < 0 || hasMarker[k] || islandArea[k] < IslandArea)
			{
				continue;
			}
			if (middle[k] < 0 || distance[i] > distance[middle[k]])
			{
				middle[k] = i;
			}
		}
		foreach (int i in middle)
		{
			if (i >= 0)
			{
				places.Add(i);
			}
		}
		return places;
	}

	private static long Distance2(int a, int b)
	{
		long dx = a % N - b % N;
		long dz = a / N - b / N;
		return dx * dx + dz * dz;
	}

	// Levels the ground along the walk and makes it a three column wide grass route.
	private static void Lay(GeneratedWorld world, List<int> path, WorldNoise widths)
	{
		int count = path.Count;
		float[] known = new float[count];
		bool[] solid = new bool[count];
		for (int i = 0; i < count; i++)
		{
			solid[i] = world.Top[path[i]] != 0;
			known[i] = solid[i] ? world.Top[path[i]] : 0f;
		}
		// Over the sky the bridge goes straight from one shore to the other.
		int last = -1;
		for (int i = 0; i < count; i++)
		{
			if (!solid[i])
			{
				continue;
			}
			if (last >= 0 && i - last > 1)
			{
				for (int k = last + 1; k < i; k++)
				{
					known[k] = known[last] + (known[i] - known[last]) * (k - last) / (i - last);
				}
			}
			last = i;
		}
		float[] smooth = new float[count];
		for (int i = 0; i < count; i++)
		{
			float sum = 0f;
			int n = 0;
			for (int k = Math.Max(0, i - SmoothRadius); k <= Math.Min(count - 1, i + SmoothRadius); k++)
			{
				sum += known[k];
				n++;
			}
			smooth[i] = sum / n;
		}
		int[] height = new int[count];
		height[0] = world.Top[path[0]];
		for (int i = 1; i < count; i++)
		{
			height[i] = Math.Max(height[i - 1] - 1, Math.Min(height[i - 1] + 1, (int)Math.Round(smooth[i])));
		}
		// From the far end back, so it arrives at its own height; each step is then one block at most.
		height[count - 1] = world.Top[path[count - 1]];
		for (int i = count - 2; i >= 0; i--)
		{
			height[i] = Math.Max(height[i + 1] - 1, Math.Min(height[i + 1] + 1, height[i]));
		}
		// The ground beside the route bends towards it (closest walked column wins), and over the sky a neck of land of
		// varying width carries it, falling off a little to its sides.
		Dictionary<int, Blend> blends = new Dictionary<int, Blend>();
		for (int i = 0; i < count; i++)
		{
			int x = path[i] % N;
			int z = path[i] / N;
			float neck = NeckMin + (NeckMax - NeckMin) * (widths.Fbm(x / 30f, z / 30f, 2) + 1f) * 0.5f;
			for (int dz = -BlendRadius; dz <= BlendRadius; dz++)
			{
				for (int dx = -BlendRadius; dx <= BlendRadius; dx++)
				{
					int nx = x + dx;
					int nz = z + dz;
					float d = (float)Math.Sqrt(dx * dx + dz * dz);
					if (d > BlendRadius || nx < Margin || nz < Margin || nx >= N - Margin || nz >= N - Margin)
					{
						continue;
					}
					int j = nx + nz * N;
					if (!blends.TryGetValue(j, out Blend blend) || d < blend.Distance)
					{
						blends[j] = new Blend { Distance = d, Height = height[i], Neck = neck };
					}
				}
			}
		}
		foreach (KeyValuePair<int, Blend> pair in blends)
		{
			int j = pair.Key;
			Blend blend = pair.Value;
			bool core = blend.Distance <= 1.5f;
			if (core && world.Route[j] && blend.Distance > 0f)
			{
				// A side column of a route laid before keeps its height.
				continue;
			}
			int top = world.Top[j];
			int target;
			if (core)
			{
				target = blend.Height;
				world.Route[j] = true;
			}
			else if (top == 0)
			{
				if (blend.Distance > blend.Neck)
				{
					continue;
				}
				target = blend.Height - (int)((blend.Distance - 1.5f) * 0.4f);
			}
			else
			{
				float w = 1f - Smooth((blend.Distance - 1.5f) / (BlendRadius - 1.5f));
				target = (int)Math.Round(top + (blend.Height - top) * w);
			}
			world.Top[j] = (byte)Math.Max(40, Math.Min(245, target));
		}
	}

	private struct Blend
	{
		public float Distance;

		public int Height;

		public float Neck;
	}

	private static float Smooth(float t)
	{
		t = Math.Max(0f, Math.Min(1f, t));
		return t * t * (3f - 2f * t);
	}

	// A* over the columns, reusing its arrays for every route.
	private sealed class Search
	{
		private readonly int[] _cost = new int[N * N];

		private readonly int[] _from = new int[N * N];

		// The search a cell's cost belongs to (so the arrays need no clearing).
		private readonly int[] _round = new int[N * N];

		private readonly List<long> _heap = new List<long>();

		private int _search;

		private readonly WorldNoise _wander;

		private readonly int[] _shore;

		internal Search(WorldNoise wander, int[] shore)
		{
			_wander = wander;
			_shore = shore;
		}

		internal List<int> Find(GeneratedWorld world, int start, int goal)
		{
			_search++;
			_heap.Clear();
			int gx = goal % N;
			int gz = goal / N;
			_round[start] = _search;
			_cost[start] = 0;
			_from[start] = -1;
			Push(Heuristic(start, gx, gz), start);
			int expanded = 0;
			while (_heap.Count > 0)
			{
				long top = Pop();
				int cell = (int)(top & 0xFFFFFFFF);
				int priority = (int)(top >> 32);
				int x = cell % N;
				int z = cell / N;
				if (priority > _cost[cell] + Heuristic(cell, gx, gz))
				{
					continue;
				}
				if (cell == goal)
				{
					return Path(goal);
				}
				if (++expanded > 400000)
				{
					return null;
				}
				for (int k = 0; k < 8; k++)
				{
					int nx = x + DX[k];
					int nz = z + DZ[k];
					if (nx < Margin || nz < Margin || nx >= N - Margin || nz >= N - Margin)
					{
						continue;
					}
					int next = nx + nz * N;
					int step;
					if (world.Top[next] == 0)
					{
						if (_shore[next] > MaxBridgeHalf)
						{
							continue;
						}
						step = SkyCost;
					}
					else if (world.Top[cell] == 0)
					{
						step = LandCost;
					}
					else
					{
						int rise = Math.Abs(world.Top[next] - world.Top[cell]);
						step = LandCost + Math.Max(0, rise - 1) * StepCost;
					}
					// Routes laid before are cheap to follow, so they share the way.
					if (world.Route[next])
					{
						step = LandCost / 2;
					}
					else
					{
						step += (int)((_wander.Fbm(nx / 40f, nz / 40f, 2) + 1f) * 0.5f * Wander);
					}
					if (k >= 4)
					{
						step = step * 14 / 10;
					}
					int cost = _cost[cell] + step;
					if (_round[next] == _search && _cost[next] <= cost)
					{
						continue;
					}
					_round[next] = _search;
					_cost[next] = cost;
					_from[next] = cell;
					Push(cost + Heuristic(next, gx, gz), next);
				}
			}
			return null;
		}

		// Octile distance, somewhat more than the cheapest possible rest of the way (a faster search).
		private static int Heuristic(int cell, int gx, int gz)
		{
			int dx = Math.Abs(cell % N - gx);
			int dz = Math.Abs(cell / N - gz);
			int straight = Math.Abs(dx - dz);
			int diagonal = Math.Min(dx, dz);
			return (straight * LandCost + diagonal * LandCost * 14 / 10) * 6 / 5;
		}

		private List<int> Path(int goal)
		{
			List<int> path = new List<int>();
			for (int at = goal; at >= 0; at = _from[at])
			{
				path.Add(at);
			}
			path.Reverse();
			return path;
		}

		private void Push(int priority, int cell)
		{
			_heap.Add(((long)priority << 32) | (uint)cell);
			int i = _heap.Count - 1;
			while (i > 0)
			{
				int parent = (i - 1) / 2;
				if (_heap[parent] <= _heap[i])
				{
					break;
				}
				long t = _heap[parent];
				_heap[parent] = _heap[i];
				_heap[i] = t;
				i = parent;
			}
		}

		private long Pop()
		{
			long top = _heap[0];
			int lastIndex = _heap.Count - 1;
			_heap[0] = _heap[lastIndex];
			_heap.RemoveAt(lastIndex);
			int i = 0;
			while (true)
			{
				int left = i * 2 + 1;
				int right = left + 1;
				int smallest = i;
				if (left < _heap.Count && _heap[left] < _heap[smallest])
				{
					smallest = left;
				}
				if (right < _heap.Count && _heap[right] < _heap[smallest])
				{
					smallest = right;
				}
				if (smallest == i)
				{
					break;
				}
				long t = _heap[smallest];
				_heap[smallest] = _heap[i];
				_heap[i] = t;
				i = smallest;
			}
			return top;
		}
	}
}
