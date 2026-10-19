using System.Collections.Generic;
using UnityEngine;

namespace CastleStoryPlus.Upgrades;

// A mesh built in code for the worn armour, one submesh per material, flat shaded like the armour concept (every face
// has its own vertices and normal). The shapes are the concept's three.js ones: boxes, eight-sided cylinders and
// cones, octahedrons and flat outlines pushed out to a thickness. Each shape is placed with a matrix from its own
// space (the concept's units, x right, y up, z forward) into the mesh's space; faces are wound from their normal
// after that, so a mirrored or skewed placement still shows the right side. A skinned piece gives every vertex the
// bone weights in Weight when it is added.
internal sealed class ArmourMesh
{
	private readonly List<Vector3> _vertices = new List<Vector3>();

	private readonly List<Vector3> _normals = new List<Vector3>();

	private readonly List<Vector2> _uvs = new List<Vector2>();

	private readonly List<BoneWeight> _weights = new List<BoneWeight>();

	private readonly List<Material> _materials = new List<Material>();

	private readonly List<List<int>> _triangles = new List<List<int>>();

	// The bone weights of the vertices added from now on (skinned pieces).
	public BoneWeight Weight;

	public int VertexCount => _vertices.Count;

	public Material[] Materials => _materials.ToArray();

	private List<int> Slot(Material material)
	{
		int index = _materials.IndexOf(material);
		if (index < 0)
		{
			_materials.Add(material);
			_triangles.Add(new List<int>());
			index = _materials.Count - 1;
		}
		return _triangles[index];
	}

	// One triangle as it is (the body's own surface, wound as the body is), with its own vertices.
	public void Triangle(Material material, Vector3[] points, Vector3[] normals, Vector2[] uvs, BoneWeight[] weights)
	{
		List<int> triangles = Slot(material);
		for (int k = 0; k < 3; k++)
		{
			triangles.Add(_vertices.Count);
			_vertices.Add(points[k]);
			_normals.Add(normals[k]);
			_uvs.Add(uvs[k]);
			_weights.Add(weights[k]);
		}
	}

	// A flat convex face in shape space, its normal pointing out.
	private void Face(Material material, Matrix4x4 place, Vector3[] points, Vector3 normal)
	{
		Matrix4x4 normalMatrix = place.inverse.transpose;
		Vector3 n = normalMatrix.MultiplyVector(normal).normalized;
		Vector3[] placed = new Vector3[points.Length];
		for (int i = 0; i < points.Length; i++)
		{
			placed[i] = place.MultiplyPoint3x4(points[i]);
		}
		// Newell's normal of the placed outline: a triangle faces along the cross of its first two edges.
		Vector3 newell = Vector3.zero;
		for (int i = 0; i < placed.Length; i++)
		{
			newell += Vector3.Cross(placed[i], placed[(i + 1) % placed.Length]);
		}
		bool reverse = Vector3.Dot(newell, n) < 0f;
		List<int> triangles = Slot(material);
		int first = _vertices.Count;
		for (int i = 0; i < placed.Length; i++)
		{
			_vertices.Add(placed[i]);
			_normals.Add(n);
			_uvs.Add(Vector2.zero);
			_weights.Add(Weight);
		}
		for (int i = 1; i + 1 < placed.Length; i++)
		{
			triangles.Add(first);
			if (reverse)
			{
				triangles.Add(first + i + 1);
				triangles.Add(first + i);
			}
			else
			{
				triangles.Add(first + i);
				triangles.Add(first + i + 1);
			}
		}
	}

	// The concept's mesh transform: position, rotation in radians (three.js order XYZ) and scale.
	public static Matrix4x4 Local(Vector3 position, Vector3 rotation, Vector3 scale)
	{
		Quaternion q = Quaternion.AngleAxis(rotation.x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(rotation.y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rotation.z * Mathf.Rad2Deg, Vector3.forward);
		return Matrix4x4.TRS(position, q, scale);
	}

	// three.js BoxGeometry(w, h, d) at a position.
	public void Box(Material material, Matrix4x4 place, Vector3 size, Vector3 position, Vector3 rotation = default)
	{
		Matrix4x4 m = place * Local(position, rotation, size);
		for (int axis = 0; axis < 3; axis++)
		{
			int u = (axis + 1) % 3;
			int v = (axis + 2) % 3;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				Vector3 normal = Vector3.zero;
				normal[axis] = sign;
				Vector3[] corners = new Vector3[4];
				for (int corner = 0; corner < 4; corner++)
				{
					Vector3 p = Vector3.zero;
					p[axis] = sign * 0.5f;
					p[u] = (corner == 1 || corner == 2) ? 0.5f : -0.5f;
					p[v] = (corner >= 2) ? 0.5f : -0.5f;
					corners[corner] = p;
				}
				Face(material, m, corners, normal);
			}
		}
	}

	// three.js CylinderGeometry(top, bottom, height, segments) at a position: segment i at angle 2πi/n, x = r sin,
	// z = r cos, so an eight-sided one has a corner at the front.
	public void Cylinder(Material material, Matrix4x4 place, float top, float bottom, float height, int segments, Vector3 position, Vector3 rotation = default)
	{
		Matrix4x4 m = place * Local(position, rotation, Vector3.one);
		float half = height / 2f;
		Vector3[] upper = new Vector3[segments];
		Vector3[] lower = new Vector3[segments];
		for (int i = 0; i < segments; i++)
		{
			float a = i * Mathf.PI * 2f / segments;
			upper[i] = new Vector3(top * Mathf.Sin(a), half, top * Mathf.Cos(a));
			lower[i] = new Vector3(bottom * Mathf.Sin(a), -half, bottom * Mathf.Cos(a));
		}
		for (int i = 0; i < segments; i++)
		{
			int j = (i + 1) % segments;
			float mid = (i + 0.5f) * Mathf.PI * 2f / segments;
			Vector3 radial = new Vector3(Mathf.Sin(mid), 0f, Mathf.Cos(mid));
			// The side's slope: leaning in by (bottom - top) over the height.
			Vector3 normal = (radial * height + Vector3.up * (bottom - top)).normalized;
			if (top <= 0f)
			{
				Face(material, m, new Vector3[3] { upper[i], lower[i], lower[j] }, normal);
			}
			else if (bottom <= 0f)
			{
				Face(material, m, new Vector3[3] { upper[i], upper[j], lower[i] }, normal);
			}
			else
			{
				Face(material, m, new Vector3[4] { upper[i], upper[j], lower[j], lower[i] }, normal);
			}
		}
		if (top > 0f)
		{
			Face(material, m, upper, Vector3.up);
		}
		if (bottom > 0f)
		{
			Face(material, m, lower, Vector3.down);
		}
	}

	// three.js OctahedronGeometry(radius) at a position.
	public void Octahedron(Material material, Matrix4x4 place, float radius, Vector3 position)
	{
		Matrix4x4 m = place * Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * radius);
		for (int sx = -1; sx <= 1; sx += 2)
		{
			for (int sy = -1; sy <= 1; sy += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					Vector3[] corners = new Vector3[3] { new Vector3(sx, 0f, 0f), new Vector3(0f, sy, 0f), new Vector3(0f, 0f, sz) };
					Face(material, m, corners, new Vector3(sx, sy, sz).normalized);
				}
			}
		}
	}

	// A convex outline in the xy plane pushed out from z = back to z = front (three.js ExtrudeGeometry).
	public void Extrude(Material material, Matrix4x4 place, List<Vector2> outline, float back, float front)
	{
		int n = outline.Count;
		Vector2 centre = Vector2.zero;
		foreach (Vector2 p in outline)
		{
			centre += p;
		}
		centre /= n;
		Vector3[] frontFace = new Vector3[n];
		Vector3[] backFace = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			frontFace[i] = new Vector3(outline[i].x, outline[i].y, front);
			backFace[i] = new Vector3(outline[i].x, outline[i].y, back);
		}
		Face(material, place, frontFace, Vector3.forward);
		Face(material, place, backFace, Vector3.back);
		for (int i = 0; i < n; i++)
		{
			Vector2 a = outline[i];
			Vector2 b = outline[(i + 1) % n];
			if ((b - a).sqrMagnitude < 1e-8f)
			{
				continue;
			}
			Vector2 side = new Vector2(b.y - a.y, a.x - b.x).normalized;
			if (Vector2.Dot(side, (a + b) / 2f - centre) < 0f)
			{
				side = -side;
			}
			Face(material, place, new Vector3[4] { new Vector3(a.x, a.y, back), new Vector3(b.x, b.y, back), new Vector3(b.x, b.y, front), new Vector3(a.x, a.y, front) }, new Vector3(side.x, side.y, 0f));
		}
	}

	// The concept's heaterShape(s): a flat top, straight sides down to 0.4 s, curving to the point at -3.1 s.
	public static List<Vector2> Heater(float s)
	{
		List<Vector2> outline = new List<Vector2>();
		outline.Add(new Vector2(-2f * s, 2.4f * s));
		outline.Add(new Vector2(2f * s, 2.4f * s));
		outline.Add(new Vector2(2f * s, 0.4f * s));
		Curve(outline, new Vector2(2f * s, 0.4f * s), new Vector2(2f * s, -2f * s), new Vector2(0f, -3.1f * s));
		Curve(outline, new Vector2(0f, -3.1f * s), new Vector2(-2f * s, -2f * s), new Vector2(-2f * s, 0.4f * s));
		return outline;
	}

	// A quadratic curve in 6 steps (the concept's curveSegments), without its first point.
	private static void Curve(List<Vector2> outline, Vector2 from, Vector2 control, Vector2 to)
	{
		const int Steps = 6;
		for (int i = 1; i <= Steps; i++)
		{
			float t = (float)i / Steps;
			outline.Add((1f - t) * (1f - t) * from + 2f * (1f - t) * t * control + t * t * to);
		}
	}

	public Mesh Bake(string name, Matrix4x4[] bindposes)
	{
		Mesh mesh = new Mesh();
		mesh.name = "CastleStoryPlus " + name;
		mesh.SetVertices(_vertices);
		mesh.SetNormals(_normals);
		mesh.SetUVs(0, _uvs);
		List<Color> colours = new List<Color>(_vertices.Count);
		for (int i = 0; i < _vertices.Count; i++)
		{
			colours.Add(Color.white);
		}
		mesh.SetColors(colours);
		if (bindposes != null)
		{
			mesh.boneWeights = _weights.ToArray();
			mesh.bindposes = bindposes;
		}
		mesh.subMeshCount = _triangles.Count;
		for (int i = 0; i < _triangles.Count; i++)
		{
			mesh.SetTriangles(_triangles[i], i);
		}
		mesh.RecalculateBounds();
		return mesh;
	}
}
