using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CastleStoryPlus.Treasure;

// A beast's model: smooth meshes made by tools/beasts (signed distance shapes melted together, meshed with surface
// nets) and shipped as Assets/Beasts/<name>.json with its baked texture <name>.png (colours, patterns and ambient
// occlusion, one atlas cell per triangle). Each part (body, legs, arms, head, tail) is its own mesh on its own
// transform, turning about its pivot, so it can be animated. Positions are in blocks: x forward, y up from the
// ground, z to the side; the vertices come three per triangle, each with its own UV. Loaded once per kind; every
// unit wearing it shares the meshes and the material.
internal sealed class BeastMesh
{
	private sealed class Part
	{
		public string Name;

		public Vector3 Pivot;

		public Mesh Mesh;
	}

	private readonly string _name;

	private List<Part> _parts;

	private Texture2D _texture;

	private Material _material;

	private bool _failed;

	internal BeastMesh(string name)
	{
		_name = name;
	}

	// Builds the beast under the parent: one child per part, named after it. Null when the file could not be read.
	internal Dictionary<string, Transform> Build(Transform parent, Material template)
	{
		if (!Load())
		{
			return null;
		}
		Material material = MakeMaterial(template);
		Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
		foreach (Part part in _parts)
		{
			GameObject go = new GameObject(part.Name);
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			go.transform.localPosition = ToLocal(part.Pivot);
			go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			parts[part.Name] = go.transform;
		}
		return parts;
	}

	// Blocks (x forward, y up, z side) to the transform's axes (z forward, y up, x side).
	private static Vector3 ToLocal(Vector3 v)
	{
		return new Vector3(v.z, v.y, v.x);
	}

	private bool Load()
	{
		if (_parts != null || _failed)
		{
			return _parts != null;
		}
		string file = Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Assets"), "Beasts"), _name + ".json");
		try
		{
			JObject data = JObject.Parse(File.ReadAllText(file));
			_texture = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = _name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
			_texture.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(file), (string)data["texture"])));
			List<Part> parts = new List<Part>();
			foreach (JObject part in (JArray)data["parts"])
			{
				parts.Add(ReadPart(part));
			}
			_parts = parts;
			return true;
		}
		catch (Exception ex)
		{
			_failed = true;
			Plugin.Log.LogError("Treasure: could not load the model " + file + ": " + ex.Message);
			return false;
		}
	}

	private static Part ReadPart(JObject part)
	{
		JArray pivotJson = (JArray)part["pivot"];
		Vector3 pivot = new Vector3((float)pivotJson[0], (float)pivotJson[1], (float)pivotJson[2]);
		JArray v = (JArray)part["v"];
		JArray n = (JArray)part["n"];
		JArray uv = (JArray)part["uv"];
		int count = v.Count / 3;
		Vector3[] vertices = new Vector3[count];
		Vector3[] normals = new Vector3[count];
		Vector2[] uvs = new Vector2[count];
		for (int i = 0; i < count; i++)
		{
			Vector3 at = new Vector3((float)v[i * 3], (float)v[i * 3 + 1], (float)v[i * 3 + 2]);
			vertices[i] = ToLocal(at - pivot);
			normals[i] = ToLocal(new Vector3((float)n[i * 3], (float)n[i * 3 + 1], (float)n[i * 3 + 2]));
			uvs[i] = new Vector2((float)uv[i * 2], (float)uv[i * 2 + 1]);
		}
		// Swapping x and z mirrors the shape, so every triangle's winding is turned round to keep facing out.
		int[] triangles = new int[count - count % 3];
		for (int i = 0; i + 2 < count; i += 3)
		{
			triangles[i] = i;
			triangles[i + 1] = i + 2;
			triangles[i + 2] = i + 1;
		}
		Mesh mesh = new Mesh { name = (string)part["name"] };
		mesh.vertices = vertices;
		mesh.normals = normals;
		mesh.uv = uvs;
		mesh.triangles = triangles;
		mesh.RecalculateBounds();
		return new Part { Name = (string)part["name"], Pivot = pivot, Mesh = mesh };
	}

	// A plain lit material showing the baked texture.
	private Material MakeMaterial(Material template)
	{
		if (_material != null)
		{
			return _material;
		}
		// A plain diffuse shader: the unit's own material draws extra maps (its corruption veins) that would show as
		// stripes over the palette; it is used only when no plain shader is in the game.
		Material material = null;
		foreach (string name in new[] { "Legacy Shaders/Diffuse", "Diffuse", "Standard" })
		{
			Shader shader = Shader.Find(name);
			if (shader != null)
			{
				material = new Material(shader);
				break;
			}
		}
		if (material == null && template != null && template.HasProperty("_MainTex"))
		{
			material = new Material(template);
		}
		if (material == null)
		{
			return null;
		}
		material.name = _name;
		material.mainTexture = _texture;
		material.mainTextureScale = Vector2.one;
		material.mainTextureOffset = Vector2.zero;
		if (material.HasProperty("_Color"))
		{
			material.color = Color.white;
		}
		_material = material;
		return material;
	}
}
