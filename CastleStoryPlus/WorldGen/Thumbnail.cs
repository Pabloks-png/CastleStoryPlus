using System;

namespace CastleStoryPlus.WorldGen;

// The map's picture in the menu: the world seen from above (north up) over a sky, shaded by the slopes, with the
// starting points, neutral crystals and corruptron spawn points marked.
internal static class Thumbnail
{
	internal const int Width = 1280;

	internal const int Height = 720;

	internal static byte[] Encode(GeneratedWorld world)
	{
		return PngWriter.Encode(Render(world), Width, Height);
	}

	// Width * Height * 4 bytes, the first row at the top.
	internal static byte[] Render(GeneratedWorld world)
	{
		byte[] rgba = new byte[Width * Height * 4];
		Bounds(world, out int minX, out int minZ, out int maxX, out int maxZ);
		float span = Math.Max(maxX - minX, maxZ - minZ) + 40f;
		float scale = span / (Height - 40f);
		float centerX = (minX + maxX) * 0.5f;
		float centerZ = (minZ + maxZ) * 0.5f;
		for (int py = 0; py < Height; py++)
		{
			for (int px = 0; px < Width; px++)
			{
				int o = (px + py * Width) * 4;
				int x = (int)Math.Floor(centerX + (px - Width * 0.5f) * scale);
				int z = (int)Math.Floor(centerZ + (Height * 0.5f - py) * scale);
				float r;
				float g;
				float b;
				if (world.Solid(x, z))
				{
					Ground(world, x, z, out r, out g, out b);
				}
				else
				{
					// Sky, lighter towards the bottom.
					float t = (float)py / Height;
					r = 0.45f + 0.3f * t;
					g = 0.66f + 0.2f * t;
					b = 0.86f + 0.1f * t;
				}
				rgba[o] = Byte(r);
				rgba[o + 1] = Byte(g);
				rgba[o + 2] = Byte(b);
				rgba[o + 3] = 255;
			}
		}
		foreach (GeneratedWorld.Marker marker in world.Markers)
		{
			float px = Width * 0.5f + (marker.X + 0.5f - centerX) / scale;
			float py = Height * 0.5f - (marker.Z + 0.5f - centerZ) / scale;
			if (marker.Group == WorldGenerator.StartGroup)
			{
				Dot(rgba, px, py, 9f, 255, 255, 255);
				Dot(rgba, px, py, 6.5f, 30, 110, 230);
			}
			else if (marker.Group == WorldGenerator.CrystalGroup)
			{
				Dot(rgba, px, py, 5f, 255, 255, 255);
				Dot(rgba, px, py, 3.5f, 90, 220, 230);
			}
			else
			{
				Dot(rgba, px, py, 5f, 40, 10, 50);
				Dot(rgba, px, py, 3.5f, 170, 60, 200);
			}
		}
		return rgba;
	}

	private static void Bounds(GeneratedWorld world, out int minX, out int minZ, out int maxX, out int maxZ)
	{
		const int N = GeneratedWorld.Size;
		minX = N;
		minZ = N;
		maxX = 0;
		maxZ = 0;
		for (int z = 0; z < N; z++)
		{
			for (int x = 0; x < N; x++)
			{
				if (world.Top[x + z * N] == 0)
				{
					continue;
				}
				minX = Math.Min(minX, x);
				maxX = Math.Max(maxX, x);
				minZ = Math.Min(minZ, z);
				maxZ = Math.Max(maxZ, z);
			}
		}
		if (minX > maxX)
		{
			minX = 0;
			minZ = 0;
			maxX = N;
			maxZ = N;
		}
	}

	private static void Ground(GeneratedWorld world, int x, int z, out float r, out float g, out float b)
	{
		const int N = GeneratedWorld.Size;
		int i = x + z * N;
		WorldBiome biome = world.Settings.Biome;
		switch (world.Route[i] ? 2 : world.Crust[i])
		{
			case 3:
				r = 0.93f;
				g = 0.95f;
				b = 0.98f;
				break;
			case 4:
				if (world.Forest[i])
				{
					Pick(biome, 0.2f, 0.38f, 0.15f, 0.26f, 0.38f, 0.34f, 0.42f, 0.45f, 0.2f, out r, out g, out b);
				}
				else
				{
					Pick(biome, 0.5f, 0.43f, 0.33f, 0.56f, 0.53f, 0.52f, 0.72f, 0.57f, 0.4f, out r, out g, out b);
				}
				break;
			case 2:
				// The routes: lighter than the meadows around them.
				Pick(biome, 0.72f, 0.8f, 0.45f, 0.97f, 0.97f, 0.92f, 0.93f, 0.84f, 0.6f, out r, out g, out b);
				break;
			case 7:
				Pick(biome, 0.37f, 0.56f, 0.21f, 0.8f, 0.84f, 0.88f, 0.78f, 0.66f, 0.43f, out r, out g, out b);
				break;
			default:
				Pick(biome, 0.52f, 0.68f, 0.31f, 0.87f, 0.9f, 0.93f, 0.85f, 0.75f, 0.5f, out r, out g, out b);
				break;
		}
		// Light from the north west, brighter up high.
		int top = world.Top[i];
		int west = world.Solid(x - 1, z + 1) ? world.Top[x - 1 + (z + 1) * N] : top;
		float shade = 1f + (west - top) * -0.07f;
		shade = Math.Max(0.55f, Math.Min(1.35f, shade)) * (0.75f + (top - 100) / 320f);
		r *= shade;
		g *= shade;
		b *= shade;
	}

	private static void Pick(WorldBiome biome, float gr, float gg, float gb, float wr, float wg, float wb, float dr, float dg, float db, out float r, out float g, out float b)
	{
		switch (biome)
		{
			case WorldBiome.Winter:
				r = wr;
				g = wg;
				b = wb;
				break;
			case WorldBiome.Desert:
				r = dr;
				g = dg;
				b = db;
				break;
			default:
				r = gr;
				g = gg;
				b = gb;
				break;
		}
	}

	private static void Dot(byte[] rgba, float cx, float cy, float radius, byte r, byte g, byte b)
	{
		for (int y = (int)(cy - radius); y <= (int)(cy + radius); y++)
		{
			for (int x = (int)(cx - radius); x <= (int)(cx + radius); x++)
			{
				if (x < 0 || y < 0 || x >= Width || y >= Height || (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius)
				{
					continue;
				}
				int o = (x + y * Width) * 4;
				rgba[o] = r;
				rgba[o + 1] = g;
				rgba[o + 2] = b;
			}
		}
	}

	private static byte Byte(float v)
	{
		return (byte)Math.Max(0, Math.Min(255, (int)(v * 255f)));
	}
}
