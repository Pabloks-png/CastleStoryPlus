using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleStoryPlus.Metallurgy;

// How steel and glass ingots look, on stockpiles and as loose items. Steel takes the game's glass ingot look (a pale
// grey, smooth metal), laid out on a stockpile as the iron and glass ingots are; it used to lie there as the
// game's terracotta, pots in a clay texture that looked like wood. Glass gets a clear, shiny light-blue material in
// its place, which reads as glass. Done once on the factories' templates (stockpiles and the glass ingot item), so
// everything made from them looks so.
internal static class IngotLooks
{
	private static readonly HashSet<GameObject> Done = new HashSet<GameObject>();

	private static readonly Dictionary<Material, Material> Glassy = new Dictionary<Material, Material>();

	// The clear glass back to the game's glass material it replaced (a stockpile copied from one already done, the
	// large stockpile, has the clear glass on its glass track; its steel still takes the old look).
	private static readonly Dictionary<Material, Material> Originals = new Dictionary<Material, Material>();

	internal static Material Original(Material material)
	{
		return (material != null && Originals.TryGetValue(material, out Material original)) ? original : material;
	}

	// The clear glass: Unity's standard shader in its transparent mode; the original when the game has no such shader.
	internal static Material GlassLook(Material original)
	{
		if (original == null || Originals.ContainsKey(original))
		{
			return original;
		}
		if (Glassy.TryGetValue(original, out Material glass) && glass != null)
		{
			return glass;
		}
		Shader standard = Shader.Find("Standard");
		if (standard == null)
		{
			Plugin.Log.LogWarning("Metallurgy: no standard shader for the glass look; glass keeps its own");
			Glassy[original] = original;
			return original;
		}
		glass = new Material(standard);
		glass.name = "CastleStoryPlus glass";
		glass.color = new Color(0.72f, 0.9f, 1f, 0.42f);
		glass.SetFloat("_Mode", 3f);
		glass.SetInt("_SrcBlend", (int)BlendMode.One);
		glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
		glass.SetInt("_ZWrite", 0);
		glass.DisableKeyword("_ALPHATEST_ON");
		glass.DisableKeyword("_ALPHABLEND_ON");
		glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
		glass.SetFloat("_Glossiness", 0.95f);
		glass.SetFloat("_Metallic", 0.05f);
		glass.renderQueue = 3000;
		Glassy[original] = glass;
		Originals[glass] = original;
		return glass;
	}

	// A stockpile template: its steel track ("TerraCotta") gets the glass track's ingots (their places and their
	// material), then the glass track the clear glass.
	internal static void Storage(GameObject storage)
	{
		if (storage == null || !Done.Add(storage))
		{
			return;
		}
		Transform steel = Find(storage.transform, "TerraCotta");
		Transform glass = Find(storage.transform, "Glass");
		if (steel == null || glass == null)
		{
			return;
		}
		steel.localPosition = glass.localPosition;
		steel.localRotation = glass.localRotation;
		steel.localScale = glass.localScale;
		List<Transform> from = Pieces(glass);
		List<Transform> to = Pieces(steel);
		for (int i = 0; i < to.Count && i < from.Count; i++)
		{
			to[i].localPosition = from[i].localPosition;
			to[i].localRotation = from[i].localRotation;
			to[i].localScale = from[i].localScale;
			MeshFilter source = from[i].GetComponent<MeshFilter>();
			MeshFilter target = to[i].GetComponent<MeshFilter>();
			if (source != null && target != null)
			{
				target.sharedMesh = source.sharedMesh;
			}
			Renderer sourceRenderer = from[i].GetComponent<Renderer>();
			Renderer targetRenderer = to[i].GetComponent<Renderer>();
			if (sourceRenderer != null && targetRenderer != null)
			{
				Material[] materials = sourceRenderer.sharedMaterials;
				for (int m = 0; m < materials.Length; m++)
				{
					materials[m] = Original(materials[m]);
				}
				targetRenderer.sharedMaterials = materials;
			}
		}
		foreach (Transform piece in from)
		{
			Glassify(piece.gameObject);
		}
		Plugin.Log.LogInfo("Metallurgy: steel and glass ingots on " + storage.name + " (" + to.Count + " pieces)");
	}

	// The loose steel ingot: the glass ingot's materials (call before the glass item gets its new look).
	internal static void SteelItem(GameObject steel, GameObject glassItem)
	{
		if (steel == null || glassItem == null)
		{
			return;
		}
		Renderer source = glassItem.GetComponentInChildren<Renderer>(true);
		if (source == null)
		{
			return;
		}
		foreach (Renderer renderer in steel.GetComponentsInChildren<Renderer>(true))
		{
			Material[] materials = renderer.sharedMaterials;
			for (int i = 0; i < materials.Length; i++)
			{
				materials[i] = Original(source.sharedMaterials[Mathf.Min(i, source.sharedMaterials.Length - 1)]);
			}
			renderer.sharedMaterials = materials;
		}
	}

	// The loose glass ingot.
	internal static void GlassItem(GameObject glassItem)
	{
		if (glassItem != null && Done.Add(glassItem))
		{
			Glassify(glassItem);
		}
	}

	private static void Glassify(GameObject go)
	{
		foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
		{
			Material[] materials = renderer.sharedMaterials;
			for (int i = 0; i < materials.Length; i++)
			{
				materials[i] = GlassLook(materials[i]);
			}
			renderer.sharedMaterials = materials;
		}
	}

	// A track's pieces in their order (the children with a mesh).
	private static List<Transform> Pieces(Transform track)
	{
		List<Transform> pieces = new List<Transform>();
		foreach (Transform child in track)
		{
			if (child.GetComponent<MeshFilter>() != null)
			{
				pieces.Add(child);
			}
		}
		return pieces;
	}

	private static Transform Find(Transform parent, string name)
	{
		foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
		{
			if (child.name == name)
			{
				return child;
			}
		}
		return null;
	}
}
