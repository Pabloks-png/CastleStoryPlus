using UnityEngine;

namespace CastleStoryPlus.Upgrades;

// The materials of the worn armour, in the colours of the armour concept ("Forge & Armoury Models"): the tier metals
// (iron, steel, crystal with a blue glow), gold, leather, wood, the shield's blue, the gambeson's quilted cloth, the
// chainmail's rings and the plate's purple rune. All on Unity's Standard shader (in the game's build, always
// included), which takes the concept's colour, picture, metalness, smoothness and glow as they are; Legacy
// Specular or Diffuse if Standard is missing. Made once per kind and tier and shared by every unit.
internal static class ArmourMaterials
{
	internal const int Metal = 0;

	internal const int Dark = 1;

	internal const int Black = 2;

	internal const int Red = 3;

	internal const int Gold = 4;

	internal const int Leather = 5;

	internal const int Plank = 6;

	internal const int WoodDark = 7;

	internal const int Blue = 8;

	internal const int Cloth = 9;

	internal const int Rings = 10;

	internal const int Quilt = 11;

	internal const int Rune = 12;

	internal const int RuneGlow = 13;

	private const int KindCount = 14;

	// Tier metal and its darker trim: iron, steel, crystal (index 0 unused).
	private static readonly int[] MetalColours = new int[4] { 0x8e969d, 0x8e969d, 0xe4eaf0, 0x58c4ff };

	private static readonly int[] DarkColours = new int[4] { 0x5b636b, 0x5b636b, 0x8d98a4, 0x2a7fc4 };

	// The chainmail's rings and the dark between them.
	private static readonly int[] RingColours = new int[4] { 0x8e969d, 0x8e969d, 0xd2dae2, 0x58c4ff };

	private static readonly int[] RingDarkColours = new int[4] { 0x4a5158, 0x4a5158, 0x6d7884, 0x1b5f99 };

	private static readonly Material[,] Made = new Material[KindCount, 4];

	private static Shader _lit;

	private static bool _standard;

	private static bool _logged;

	// The material of a kind; tier only matters for the tier metals and the rings.
	internal static Material Get(int kind, int tier)
	{
		tier = Mathf.Clamp(tier, 1, 3);
		if (kind != Metal && kind != Dark && kind != Rings)
		{
			tier = 1;
		}
		if (Made[kind, tier] != null)
		{
			return Made[kind, tier];
		}
		Material material;
		switch (kind)
		{
			case Metal:
				material = tier switch
				{
					1 => Lit("iron", Hex(MetalColours[1]), 0.55f, 0.6f, Color.black, null),
					2 => Lit("steel", Hex(MetalColours[2]), 0.35f, 0.3f, Color.black, null),
					_ => Lit("crystal", Hex(MetalColours[3]), 0.3f, 0.18f, Hex(0x1b68a8) * 0.55f, null)
				};
				break;
			case Dark:
				material = tier switch
				{
					1 => Lit("dark iron", Hex(DarkColours[1]), 0.5f, 0.6f, Color.black, null),
					2 => Lit("dark steel", Hex(DarkColours[2]), 0.35f, 0.38f, Color.black, null),
					_ => Lit("dark crystal", Hex(DarkColours[3]), 0.4f, 0.25f, Hex(0x0f3f70) * 0.4f, null)
				};
				break;
			case Black:
				material = Lit("black", Hex(0x15161a), 0f, 0.75f, Color.black, null);
				break;
			case Red:
				material = Lit("plume", Hex(0xd6453a), 0f, 0.75f, Color.black, null);
				break;
			case Gold:
				material = Lit("gold", Hex(0xe6a823), 0.7f, 0.35f, Color.black, null);
				break;
			case Leather:
				material = Lit("leather", Hex(0x6b3f1f), 0f, 0.75f, Color.black, null);
				break;
			case Plank:
				material = Lit("plank", Hex(0xa77443), 0f, 0.75f, Color.black, null);
				break;
			case WoodDark:
				material = Lit("dark wood", Hex(0x5e3b1b), 0f, 0.75f, Color.black, null);
				break;
			case Blue:
				material = Lit("shield blue", Hex(0x2f5fa8), 0f, 0.75f, Color.black, null);
				break;
			case Cloth:
				material = Lit("cloth", Hex(0xcfa86a), 0f, 0.75f, Color.black, null);
				break;
			case Rings:
				material = Lit("chainmail " + UpgradeLines.TierNames[tier], Color.white, (tier == 2) ? 0.8f : 0.5f, 0.4f, (tier == 3) ? Hex(0x0f3f70) * 0.5f : Color.black, RingTexture(tier));
				break;
			case Quilt:
				material = Lit("gambeson", Color.white, 0f, 0.95f, Color.black, QuiltTexture());
				break;
			case Rune:
				material = Lit("rune", Hex(0xc07bff), 0f, 0.75f, Hex(0x9b4dff) * 1.4f, null);
				break;
			default:
				material = Glow();
				break;
		}
		Made[kind, tier] = material;
		return material;
	}

	private static Color Hex(int rgb)
	{
		return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
	}

	private static Shader LitShader()
	{
		if (_lit != null)
		{
			return _lit;
		}
		Shader standard = Shader.Find("Standard");
		_standard = standard != null && standard.isSupported;
		_lit = _standard ? standard : (Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse"));
		if (!_logged)
		{
			_logged = true;
			Plugin.Log.LogInfo("ArmourModels: armour drawn with " + ((_lit != null) ? _lit.name : "no shader"));
		}
		return _lit;
	}

	// The concept's MeshStandardMaterial: colour (or picture), metalness, roughness, glow.
	private static Material Lit(string name, Color colour, float metallic, float roughness, Color emission, Texture2D picture)
	{
		Material material = new Material(LitShader());
		material.name = "CastleStoryPlus armour " + name;
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", colour);
		}
		if (picture != null && material.HasProperty("_MainTex"))
		{
			material.SetTexture("_MainTex", picture);
		}
		if (_standard)
		{
			// No reflection probes in the game's levels: a little less metal than the concept so it does not go dark.
			material.SetFloat("_Metallic", metallic * 0.8f);
			material.SetFloat("_Glossiness", 1f - roughness);
			if (emission.maxColorComponent > 0f)
			{
				material.EnableKeyword("_EMISSION");
				material.SetColor("_EmissionColor", emission);
				material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
		}
		else
		{
			if (material.HasProperty("_SpecColor"))
			{
				material.SetColor("_SpecColor", Color.Lerp(new Color(0.1f, 0.1f, 0.1f), Color.white, metallic * (1f - roughness)));
			}
			if (material.HasProperty("_Shininess"))
			{
				material.SetFloat("_Shininess", Mathf.Lerp(0.6f, 0.08f, 1f - roughness));
			}
		}
		return material;
	}

	// See-through purple around the rune, unlit (no shadow pass, so it casts no shadow).
	private static Material Glow()
	{
		Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? LitShader();
		Material material = new Material(shader);
		material.name = "CastleStoryPlus armour rune glow";
		material.color = new Color(0.75f, 0.45f, 1f, 0.28f);
		return material;
	}

	// The concept's ringMat: 128 px, dark ground, staggered rings 16 px apart (radius 6.5, line 5).
	private static Texture2D RingTexture(int tier)
	{
		const int Size = 128;
		Color ring = Hex(RingColours[tier]);
		Color dark = Hex(RingDarkColours[tier]);
		Color[] pixels = new Color[Size * Size];
		for (int y = 0; y < Size; y++)
		{
			for (int x = 0; x < Size; x++)
			{
				// The nearest ring centre: rows every 16 px, every other row moved 8 px.
				float best = float.MaxValue;
				for (int row = (y / 16) - 1; row <= (y / 16) + 1; row++)
				{
					float cy = row * 16f;
					float shift = (((row % 2) + 2) % 2 == 1) ? 8f : 0f;
					float cx = Mathf.Round((x - shift) / 16f) * 16f + shift;
					float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
					best = Mathf.Min(best, Mathf.Abs(d - 6.5f));
				}
				// The line is 5 px wide; half a pixel of soft edge.
				float t = Mathf.Clamp01(3f - best);
				pixels[y * Size + x] = Color.Lerp(dark, ring, t);
			}
		}
		return Picture("chainmail " + UpgradeLines.TierNames[tier], Size, pixels);
	}

	// The concept's quiltMat: tan cloth with darker diagonal quilting every 32 px both ways (line 4).
	private static Texture2D QuiltTexture()
	{
		const int Size = 128;
		Color cloth = Hex(0xcfa86a);
		Color stitch = Hex(0x9a7740);
		Color[] pixels = new Color[Size * Size];
		for (int y = 0; y < Size; y++)
		{
			for (int x = 0; x < Size; x++)
			{
				int a = ((x - y) % 32 + 32) % 32;
				int b = ((x + y) % 32 + 32) % 32;
				float d = Mathf.Min(Mathf.Min(a, 32 - a), Mathf.Min(b, 32 - b)) / 1.4142f;
				float t = Mathf.Clamp01(2.5f - d);
				pixels[y * Size + x] = Color.Lerp(cloth, stitch, t);
			}
		}
		return Picture("quilt", Size, pixels);
	}

	private static Texture2D Picture(string name, int size, Color[] pixels)
	{
		Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, mipmap: true);
		texture.name = "CastleStoryPlus armour " + name;
		texture.SetPixels(pixels);
		texture.wrapMode = TextureWrapMode.Repeat;
		texture.filterMode = FilterMode.Trilinear;
		texture.anisoLevel = 4;
		texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
		return texture;
	}
}
