using System;

namespace CastleStoryPlus.WorldGen;

// Seeded 2D gradient (Perlin) noise with fractal sums. Pure arithmetic, no Unity: the same seed gives the same world
// on every machine and thread.
internal sealed class WorldNoise
{
	private static readonly float[] GradX = { 1f, -1f, 1f, -1f, 0.7071f, -0.7071f, 0.7071f, -0.7071f };

	private static readonly float[] GradY = { 0f, 0f, 1f, -1f, 0.7071f, 0.7071f, -0.7071f, -0.7071f };

	private readonly int[] _perm = new int[512];

	// Moves each noise away from the origin so different noises of one seed do not line up.
	private readonly float _offsetX;

	private readonly float _offsetY;

	internal WorldNoise(int seed)
	{
		Random random = new Random(seed);
		int[] p = new int[256];
		for (int i = 0; i < 256; i++)
		{
			p[i] = i;
		}
		for (int i = 255; i > 0; i--)
		{
			int j = random.Next(i + 1);
			int t = p[i];
			p[i] = p[j];
			p[j] = t;
		}
		for (int i = 0; i < 512; i++)
		{
			_perm[i] = p[i & 255];
		}
		_offsetX = (float)(random.NextDouble() * 4096.0);
		_offsetY = (float)(random.NextDouble() * 4096.0);
	}

	// About -1 to 1.
	internal float Sample(float x, float y)
	{
		x += _offsetX;
		y += _offsetY;
		int xi = (int)Math.Floor(x);
		int yi = (int)Math.Floor(y);
		float xf = x - xi;
		float yf = y - yi;
		xi &= 255;
		yi &= 255;
		float u = Fade(xf);
		float v = Fade(yf);
		float n00 = Dot(_perm[xi + _perm[yi]], xf, yf);
		float n10 = Dot(_perm[xi + 1 + _perm[yi]], xf - 1f, yf);
		float n01 = Dot(_perm[xi + _perm[yi + 1]], xf, yf - 1f);
		float n11 = Dot(_perm[xi + 1 + _perm[yi + 1]], xf - 1f, yf - 1f);
		float a = n00 + u * (n10 - n00);
		float b = n01 + u * (n11 - n01);
		return (a + v * (b - a)) * 1.414f;
	}

	// Octaves of noise, each twice as fine and half as strong; about -1 to 1.
	internal float Fbm(float x, float y, int octaves)
	{
		float sum = 0f;
		float amplitude = 1f;
		float total = 0f;
		for (int i = 0; i < octaves; i++)
		{
			sum += Sample(x, y) * amplitude;
			total += amplitude;
			amplitude *= 0.5f;
			x *= 2.03f;
			y *= 2.03f;
		}
		return sum / total;
	}

	// Sharp crests where the noise crosses zero (mountain ridges); 0 to 1.
	internal float Ridged(float x, float y, int octaves)
	{
		float sum = 0f;
		float amplitude = 1f;
		float total = 0f;
		float weight = 1f;
		for (int i = 0; i < octaves; i++)
		{
			float n = 1f - Math.Abs(Sample(x, y));
			n *= n * weight;
			weight = Math.Min(1f, n * 2f);
			sum += n * amplitude;
			total += amplitude;
			amplitude *= 0.5f;
			x *= 2.03f;
			y *= 2.03f;
		}
		return sum / total;
	}

	private static float Fade(float t)
	{
		return t * t * t * (t * (t * 6f - 15f) + 10f);
	}

	private static float Dot(int hash, float x, float y)
	{
		int g = hash & 7;
		return GradX[g] * x + GradY[g] * y;
	}
}
