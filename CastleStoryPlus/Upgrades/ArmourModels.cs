using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using CastleStoryPlus.UI;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Upgrades;

// Researched armour is worn by the soldiers (the units UpgradeEffects counts as soldiers) as the armour concept
// ("Forge & Armoury Models", its "On Bricktron" kits) draws it, in the team's highest tier of each line:
// - Chainmail: a mail shirt of rings (the concept's ring picture in the tier's colours) over the body and the upper
//   arms, a leather belt with a gold buckle; steel adds long sleeves and a mail coif, crystal gold trims.
// - Padded Gambeson: a quilted tan shirt with long sleeves, a padded collar (crystal metal at crystal) and metal
//   rivets, more per tier.
// - Warded Plate: a cuirass round the chest and back in the tier's metal, a ridge down the front and a strap
//   across it, a glowing purple rune gem; steel adds pauldrons, crystal a gold trim.
// - Reinforced Helmet: the game's helmet (and kettle hat) is replaced by the concept's eight-sided great helm with
//   a visor slit, a nose bar and breathing holes; steel adds a red plume, crystal a gold band and a crystal gem.
// - Shield Rims: the game's shield is replaced by the concept's heater shield: a rim in the tier's metal, a plank
//   face (iron) or a blue face with a gold cross (steel, crystal), a boss and studs.
// The body armour is the bricktron's own surface, like the concept's: the triangles of the right body parts (by the
// bones that move them) within the piece's height band, pushed out along their normals, and skinned to the same bones,
// so it follows the body and the arms in every animation and grows with a giant bricktron. Layers from the inside
// out: gambeson, mail, plate. The body is measured once per model and mapped onto the concept's bricktron (torso box
// 1.1 x 0.88 x 0.96, from y 0.28 to 1.16), so the concept's heights and sizes apply. The helm hangs on the head bone,
// the shield on the game shield's own transform (so grip and animations stay); the game's own models are hidden by
// swapping in an empty mesh and get their mesh back when the armour goes. Meshes and materials are shared per model,
// piece and tiers. Updated when the job or the gear changes, when a tier is researched or received, and when a level
// is ready; a unit that is released to the pool (death included) loses them.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(Occupation), nameof(Occupation.CurrentJob), MethodType.Setter)]
internal static class ArmourModels
{
	// The worn lines, in the order of ArmourWorn's tiers: body armour, then the helm and the shield.
	private static readonly int[] BodyLines = new int[3] { UpgradeLines.Chainmail, UpgradeLines.Gambeson, UpgradeLines.WardedPlate };

	private const int MailTier = 0;

	private const int GambesonTier = 1;

	private const int PlateTier = 2;

	private const int HelmTier = 3;

	private const int ShieldTier = 4;

	private const int TierCount = 5;

	// The body parts, by the bone that moves a vertex most.
	private const int Torso = 1;

	private const int Neck = 2;

	private const int Head = 4;

	private const int Clavicle = 8;

	private const int UpperArm = 16;

	private const int Forearm = 32;

	private const int Hand = 64;

	private const int Leg = 128;

	// The concept bricktron's torso box (Bricktron_Posed): x -0.55..0.55, y 0.28..1.16, z -0.44..0.52.
	private const float ConceptHalfWidth = 0.55f;

	private const float ConceptHalfDepth = 0.48f;

	private const float ConceptFrontShift = 0.04f;

	private const float ConceptBottom = 0.28f;

	private const float ConceptTop = 1.16f;

	// One bricktron model: its surface mapped onto the concept's bricktron, and the pieces made for it.
	private sealed class Body
	{
		public bool Valid;

		public Vector3[] Vertices;

		public Vector3[] Normals;

		public BoneWeight[] Weights;

		public int[] Triangles;

		// Per vertex: its body part and its place in concept units.
		public int[] Part;

		public Vector3[] Concept;

		public Matrix4x4 ToMesh;

		public Matrix4x4 ToConcept;

		// Mesh units per concept unit, for offsets along the normals.
		public float Unit;

		public Matrix4x4[] Bindposes;

		public int Head = -1;

		public string HeadName;

		public Vector3 HeadMin;

		public Vector3 HeadMax;

		public readonly Dictionary<int, Mesh> Suits = new Dictionary<int, Mesh>();

		public readonly Dictionary<int, Material[]> SuitMaterials = new Dictionary<int, Material[]>();

		public readonly Mesh[] Helms = new Mesh[4];

		public readonly Material[][] HelmMaterials = new Material[4][];
	}

	// One game shield model: which of its axes are the concept's right, up and front, and the shields made for it.
	private sealed class ShieldFit
	{
		public Matrix4x4 Place;

		public readonly Mesh[] Meshes = new Mesh[4];

		public readonly Material[][] Materials = new Material[4][];
	}

	private static readonly Dictionary<Mesh, Body> Bodies = new Dictionary<Mesh, Body>();

	private static readonly Dictionary<Mesh, ShieldFit> Shields = new Dictionary<Mesh, ShieldFit>();

	private static List<GameObject> _tools = new List<GameObject>();

	private static void Enable()
	{
		TeamUpgrades.Changed += Refresh;
		GameSignals.Connect(GameSignals.LevelReady, RefreshAll);
	}

	// The job was set (the gear changed, or the unit was loaded or synced).
	private static void Postfix(Occupation __instance)
	{
		Refresh(__instance.GetComponent<Labor>());
	}

	// A tier was researched (or tiers arrived from the host or a save).
	private static void Refresh(Faction faction)
	{
		foreach (Labor labor in Live<Labor>.Active())
		{
			if (labor != null && Faction.GetFaction(labor.gameObject) == faction)
			{
				Refresh(labor);
			}
		}
	}

	private static void RefreshAll()
	{
		foreach (Labor labor in Live<Labor>.Active())
		{
			Refresh(labor);
		}
	}

	internal static void Refresh(Labor labor)
	{
		if (labor == null)
		{
			return;
		}
		ArmourWorn worn = labor.GetComponent<ArmourWorn>();
		int[] tiers = new int[TierCount];
		GameObject helmet = null;
		GameObject shield = null;
		bool any = false;
		if (labor.gameObject.activeInHierarchy && UpgradeEffects.IsSoldier(labor) && IsAlive(labor))
		{
			for (int i = 0; i < BodyLines.Length; i++)
			{
				tiers[i] = TeamUpgrades.Tier(labor.gameObject, BodyLines[i]);
			}
			FindGear(labor, out helmet, out shield);
			tiers[HelmTier] = (helmet != null) ? TeamUpgrades.Tier(labor.gameObject, UpgradeLines.Helmet) : 0;
			tiers[ShieldTier] = (shield != null) ? TeamUpgrades.Tier(labor.gameObject, UpgradeLines.ShieldRims) : 0;
			if (tiers[HelmTier] == 0)
			{
				helmet = null;
			}
			if (tiers[ShieldTier] == 0)
			{
				shield = null;
			}
			foreach (int tier in tiers)
			{
				any |= tier > 0;
			}
		}
		if (worn == null)
		{
			if (!any)
			{
				return;
			}
			worn = labor.gameObject.AddComponent<ArmourWorn>();
		}
		if (worn.Shows(tiers, helmet, shield))
		{
			return;
		}
		worn.Clear();
		if (!any)
		{
			return;
		}
		SkinnedMeshRenderer skin = BodyOf(labor);
		Body body = (skin != null) ? BodyFor(skin, labor.transform) : null;
		bool fitted = body != null && body.Valid;
		int mail = tiers[MailTier];
		int gambeson = tiers[GambesonTier];
		int plate = tiers[PlateTier];
		if (fitted && (mail > 0 || gambeson > 0 || plate > 0))
		{
			worn.Add(Suit(body, skin, mail, gambeson, plate));
		}
		if (fitted && helmet != null && body.Head >= 0)
		{
			Transform head = BoneAt(skin.bones, body.Head, body.HeadName);
			if (head != null)
			{
				int tier = tiers[HelmTier];
				if (body.Helms[tier] == null)
				{
					BuildHelm(body, tier, skin.sharedMesh.name);
				}
				worn.Add(Rigid("helm", head, body.Helms[tier], body.HelmMaterials[tier], skin.enabled));
				worn.Hide(helmet);
			}
		}
		if (shield != null)
		{
			GameObject piece = ShieldPiece(shield, tiers[ShieldTier], labor.transform, skin);
			if (piece != null)
			{
				worn.Add(piece);
				worn.Hide(shield);
			}
		}
		worn.Remember(tiers, helmet, shield);
	}

	private static bool IsAlive(Labor labor)
	{
		Locomotion4 locomotion = (labor.navigation != null) ? labor.navigation.Locomotion : null;
		return locomotion == null || !locomotion.IsCorpse;
	}

	// The unit's equipped helmet (or kettle hat) and shield.
	private static void FindGear(Labor labor, out GameObject helmet, out GameObject shield)
	{
		helmet = null;
		shield = null;
		Toolbag toolbag = labor.GetComponent<Toolbag>();
		if (toolbag == null)
		{
			return;
		}
		_tools.Clear();
		toolbag.Tools(ref _tools, includeNullTools: false, includeAesthetic: true);
		foreach (GameObject tool in _tools)
		{
			FactoryImprint imprint = (tool != null) ? tool.GetComponent<FactoryImprint>() : null;
			if (imprint == null)
			{
				continue;
			}
			Factory.AssetKey key = imprint.AssetKey;
			if (key == Accessories.Helmet || key == Accessories.KettleHat)
			{
				helmet = tool;
			}
			else if (key == Accessories.Shield)
			{
				shield = tool;
			}
		}
		_tools.Clear();
	}

	// The bricktron's body: the skinned mesh with a chest bone.
	private static SkinnedMeshRenderer BodyOf(Labor labor)
	{
		SkinnedMeshRenderer first = null;
		foreach (SkinnedMeshRenderer renderer in labor.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
		{
			if (renderer.sharedMesh == null || renderer.name.StartsWith("CastleStoryPlus", StringComparison.Ordinal))
			{
				continue;
			}
			if (first == null)
			{
				first = renderer;
			}
			if (IndexOf(renderer.bones, " Spine1") >= 0)
			{
				return renderer;
			}
		}
		return first;
	}

	private static Transform BoneAt(Transform[] bones, int index, string name)
	{
		if (index >= 0 && index < bones.Length && bones[index] != null && bones[index].name == name)
		{
			return bones[index];
		}
		return null;
	}

	private static int IndexOf(Transform[] bones, string suffix)
	{
		for (int i = 0; i < bones.Length; i++)
		{
			if (bones[i] != null && bones[i].name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	// ---- pieces on the unit

	// The body armour: one skinned mesh on the body's own bones, next to the body.
	private static GameObject Suit(Body body, SkinnedMeshRenderer skin, int mail, int gambeson, int plate)
	{
		int key = mail * 16 + gambeson * 4 + plate;
		if (!body.Suits.TryGetValue(key, out Mesh mesh))
		{
			ArmourMesh built = BuildSuit(body, mail, gambeson, plate);
			mesh = built.Bake(skin.sharedMesh.name + " armour " + mail + gambeson + plate, body.Bindposes);
			body.Suits[key] = mesh;
			body.SuitMaterials[key] = built.Materials;
		}
		GameObject go = new GameObject("CastleStoryPlus armour");
		go.layer = skin.gameObject.layer;
		go.transform.SetParent(skin.transform, worldPositionStays: false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		SkinnedMeshRenderer renderer = go.AddComponent<SkinnedMeshRenderer>();
		renderer.bones = skin.bones;
		renderer.rootBone = skin.rootBone;
		renderer.quality = skin.quality;
		renderer.updateWhenOffscreen = skin.updateWhenOffscreen;
		renderer.sharedMesh = mesh;
		renderer.sharedMaterials = body.SuitMaterials[key];
		Bounds bounds = skin.localBounds;
		bounds.Expand(bounds.size.magnitude * 0.15f);
		renderer.localBounds = bounds;
		// Hidden in the fog like the body (the fog switches every renderer under the unit from now on).
		renderer.enabled = skin.enabled;
		return go;
	}

	private static GameObject Rigid(string name, Transform parent, Mesh mesh, Material[] materials, bool visible)
	{
		GameObject go = new GameObject("CastleStoryPlus armour " + name);
		go.layer = parent.gameObject.layer;
		go.transform.SetParent(parent, worldPositionStays: false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		go.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = go.AddComponent<MeshRenderer>();
		renderer.sharedMaterials = materials;
		renderer.enabled = visible;
		return go;
	}

	// The concept shield on the game shield's biggest mesh, in that mesh's space.
	private static GameObject ShieldPiece(GameObject item, int tier, Transform unit, SkinnedMeshRenderer skin)
	{
		MeshFilter target = null;
		float biggest = 0f;
		foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(includeInactive: true))
		{
			Mesh mesh = filter.sharedMesh;
			if (mesh == null || mesh == ArmourHidden.Empty || filter.name.StartsWith("CastleStoryPlus", StringComparison.Ordinal))
			{
				continue;
			}
			float size = mesh.bounds.size.sqrMagnitude;
			if (size > biggest)
			{
				biggest = size;
				target = filter;
			}
		}
		if (target == null)
		{
			return null;
		}
		Mesh game = target.sharedMesh;
		if (!Shields.TryGetValue(game, out ShieldFit fit))
		{
			fit = FitShield(game, target.transform, unit, skin);
			Shields[game] = fit;
		}
		if (fit.Meshes[tier] == null)
		{
			ArmourMesh built = new ArmourMesh();
			BuildShield(built, fit.Place, tier);
			fit.Meshes[tier] = built.Bake(game.name + " shield " + tier, null);
			fit.Materials[tier] = built.Materials;
		}
		bool visible = (skin != null) ? skin.enabled : true;
		return Rigid("shield", target.transform, fit.Meshes[tier], fit.Materials[tier], visible);
	}

	// The game shield's axes: its longest the concept's up, its thinnest the front. Which way is up and which way is
	// out is taken from the shield as the unit holds it now (up against the unit's up, out away from its body).
	private static ShieldFit FitShield(Mesh game, Transform shield, Transform unit, SkinnedMeshRenderer skin)
	{
		Bounds bounds = game.bounds;
		Vector3 size = bounds.size;
		int tall = 0;
		int thin = 0;
		for (int axis = 1; axis < 3; axis++)
		{
			if (size[axis] > size[tall])
			{
				tall = axis;
			}
			if (size[axis] < size[thin])
			{
				thin = axis;
			}
		}
		if (tall == thin)
		{
			thin = (tall + 1) % 3;
		}
		int wide = 3 - tall - thin;
		Vector3 up = Vector3.zero;
		up[tall] = 1f;
		Vector3 front = Vector3.zero;
		front[thin] = 1f;
		Vector3 right = Vector3.zero;
		right[wide] = 1f;
		float upSign = Vector3.Dot(shield.TransformDirection(up), unit.up);
		if (upSign < -0.2f)
		{
			up = -up;
		}
		Vector3 centre = shield.TransformPoint(bounds.center);
		Vector3 bodyCentre = (skin != null) ? skin.bounds.center : unit.position + unit.up * 0.5f;
		float frontSign = Vector3.Dot(shield.TransformDirection(front), centre - bodyCentre);
		if (frontSign < 0f)
		{
			front = -front;
		}
		// The concept's heater is 5.5 tall; as the concept wears it, 1.1 times the game shield.
		float scale = 1.1f * size[tall] / 5.5f;
		Matrix4x4 place = Matrix4x4.identity;
		place.SetColumn(0, right * scale);
		place.SetColumn(1, up * scale);
		place.SetColumn(2, front * scale);
		place.SetColumn(3, new Vector4(bounds.center.x, bounds.center.y, bounds.center.z, 1f));
		Plugin.Log.LogInfo("ArmourModels: shield " + game.name + " size " + size.ToString("F3") + ", up axis " + tall + " (" + upSign.ToString("F2") + " against the unit's up), front axis " + thin + " (" + frontSign.ToString("F3") + " away from the body), scale " + scale.ToString("F3"));
		return new ShieldFit { Place = place };
	}

	// ---- fitting the body

	private static int PartOf(string bone)
	{
		string name = bone.ToLowerInvariant();
		if (name.Contains("finger") || name.Contains("hand"))
		{
			return Hand;
		}
		if (name.Contains("forearm"))
		{
			return Forearm;
		}
		if (name.Contains("upperarm"))
		{
			return UpperArm;
		}
		if (name.Contains("clavicle"))
		{
			return Clavicle;
		}
		if (name.Contains("thigh") || name.Contains("calf") || name.Contains("foot") || name.Contains("toe") || name.Contains("leg"))
		{
			return Leg;
		}
		if (name.Contains("neck"))
		{
			return Neck;
		}
		if (name.Contains("head") || name.Contains("eye") || name.Contains("jaw") || name.Contains("brow") || name.Contains("mouth"))
		{
			return Head;
		}
		return Torso;
	}

	private static int Strongest(BoneWeight w)
	{
		int bone = w.boneIndex0;
		float weight = w.weight0;
		if (w.weight1 > weight)
		{
			bone = w.boneIndex1;
			weight = w.weight1;
		}
		if (w.weight2 > weight)
		{
			bone = w.boneIndex2;
			weight = w.weight2;
		}
		if (w.weight3 > weight)
		{
			bone = w.boneIndex3;
		}
		return bone;
	}

	private static Vector3 BonePosition(Matrix4x4[] bindposes, int index)
	{
		return bindposes[index].inverse.GetColumn(3);
	}

	// Measured once per model: a frame on the body (right, up, forward from the bind pose's bones), the torso's box
	// in it (the vertices moved most by the pelvis and spine) mapped onto the concept's torso box, every vertex's body
	// part and place in concept units, and the head's box.
	private static Body BodyFor(SkinnedMeshRenderer skin, Transform unit)
	{
		Mesh mesh = skin.sharedMesh;
		if (Bodies.TryGetValue(mesh, out Body body))
		{
			return body;
		}
		body = new Body();
		Bodies[mesh] = body;
		Transform[] bones = skin.bones;
		Matrix4x4[] bindposes = mesh.bindposes;
		if (bones.Length == 0 || bones.Length != bindposes.Length || !mesh.isReadable)
		{
			Plugin.Log.LogWarning("ArmourModels: " + mesh.name + " has no usable bones or cannot be read, no armour shown");
			return body;
		}
		int pelvis = IndexOf(bones, " Pelvis");
		int spine = IndexOf(bones, " Spine");
		int neck = IndexOf(bones, " Neck");
		int head = IndexOf(bones, " Head");
		int leftArm = IndexOf(bones, " L UpperArm");
		int rightArm = IndexOf(bones, " R UpperArm");
		if (leftArm < 0 || rightArm < 0)
		{
			leftArm = IndexOf(bones, " L Clavicle");
			rightArm = IndexOf(bones, " R Clavicle");
		}
		int top = (neck >= 0) ? neck : head;
		int bottom = (pelvis >= 0) ? pelvis : spine;
		Vector3 up = (top >= 0 && bottom >= 0 && top != bottom) ? BonePosition(bindposes, top) - BonePosition(bindposes, bottom) : skin.transform.InverseTransformDirection(unit.up);
		up.Normalize();
		Vector3 right = (leftArm >= 0 && rightArm >= 0) ? BonePosition(bindposes, rightArm) - BonePosition(bindposes, leftArm) : skin.transform.InverseTransformDirection(unit.right);
		right = (right - up * Vector3.Dot(right, up)).normalized;
		Vector3 forward = Vector3.Cross(right, up);
		// The body is built facing the unit's forward; turn the frame round if the cross came out backwards.
		if (Vector3.Dot(forward, skin.transform.InverseTransformDirection(unit.forward)) < 0f)
		{
			forward = -forward;
		}
		int[] partOfBone = new int[bones.Length];
		for (int i = 0; i < bones.Length; i++)
		{
			partOfBone[i] = (bones[i] != null) ? PartOf(bones[i].name) : Torso;
		}
		Vector3[] vertices = mesh.vertices;
		Vector3[] normals = mesh.normals;
		BoneWeight[] weights = mesh.boneWeights;
		int[] triangles = mesh.triangles;
		if (vertices.Length == 0 || normals.Length != vertices.Length || weights.Length != vertices.Length)
		{
			Plugin.Log.LogWarning("ArmourModels: " + mesh.name + " has no normals or bone weights, no armour shown");
			return body;
		}
		int[] part = new int[vertices.Length];
		Vector3 min = Vector3.one * float.MaxValue;
		Vector3 max = Vector3.one * float.MinValue;
		int counted = 0;
		for (int i = 0; i < vertices.Length; i++)
		{
			int bone = Strongest(weights[i]);
			part[i] = (bone >= 0 && bone < partOfBone.Length) ? partOfBone[bone] : Torso;
			if (part[i] == Torso)
			{
				Vector3 p = new Vector3(Vector3.Dot(vertices[i], right), Vector3.Dot(vertices[i], up), Vector3.Dot(vertices[i], forward));
				min = Vector3.Min(min, p);
				max = Vector3.Max(max, p);
				counted++;
			}
		}
		if (counted < 8)
		{
			Plugin.Log.LogWarning("ArmourModels: " + mesh.name + " has no torso vertices, no armour shown");
			return body;
		}
		// Torso box onto the concept's: the same size across (one scale for width and depth, so round stays round)
		// and the same height.
		Vector3 size = max - min;
		float across = (size.x / (2f * ConceptHalfWidth) + size.z / (2f * ConceptHalfDepth)) / 2f;
		float tall = size.y / (ConceptTop - ConceptBottom);
		Vector3 centre = (min + max) / 2f;
		Vector3 origin = right * centre.x + up * (min.y - ConceptBottom * tall) + forward * (centre.z - ConceptFrontShift * across);
		Matrix4x4 toMesh = Matrix4x4.identity;
		toMesh.SetColumn(0, right * across);
		toMesh.SetColumn(1, up * tall);
		toMesh.SetColumn(2, forward * across);
		toMesh.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1f));
		body.ToMesh = toMesh;
		body.ToConcept = toMesh.inverse;
		body.Unit = (2f * across + tall) / 3f;
		body.Vertices = vertices;
		body.Normals = normals;
		body.Weights = weights;
		body.Triangles = triangles;
		body.Part = part;
		body.Bindposes = bindposes;
		body.Concept = new Vector3[vertices.Length];
		Vector3 headMin = Vector3.one * float.MaxValue;
		Vector3 headMax = Vector3.one * float.MinValue;
		int headCounted = 0;
		for (int i = 0; i < vertices.Length; i++)
		{
			body.Concept[i] = body.ToConcept.MultiplyPoint3x4(vertices[i]);
			if (part[i] == Head)
			{
				headMin = Vector3.Min(headMin, body.Concept[i]);
				headMax = Vector3.Max(headMax, body.Concept[i]);
				headCounted++;
			}
		}
		if (head >= 0 && headCounted >= 8)
		{
			body.Head = head;
			body.HeadName = bones[head].name;
			body.HeadMin = headMin;
			body.HeadMax = headMax;
		}
		body.Valid = true;
		Plugin.Log.LogInfo("ArmourModels: " + mesh.name + " fitted: torso " + size.ToString("F3") + " mesh units from " + counted + " vertices, " + across.ToString("F3") + " across and " + tall.ToString("F3") + " up per concept unit; head " + ((body.Head >= 0) ? headMin.ToString("F2") + " to " + headMax.ToString("F2") + " (concept units, " + headCounted + " vertices)" : "not found, no helm"));
		return body;
	}

	// ---- body armour

	// The body's surface: the triangles whose vertices are all in the parts and within the band (concept units),
	// pushed out along their normals by offset; uv the concept's wrap round the body (0 for none).
	private static void Shell(ArmourMesh mesh, Body body, Material material, int parts, float y0, float y1, float xMin, float xMax, float offset, float uv)
	{
		Vector3[] points = new Vector3[3];
		Vector3[] normals = new Vector3[3];
		Vector2[] uvs = new Vector2[3];
		BoneWeight[] weights = new BoneWeight[3];
		float push = offset * body.Unit;
		// The concept's picture repeats twice over its uv.
		float span = 3f * uv * 2f;
		int[] triangles = body.Triangles;
		for (int t = 0; t + 2 < triangles.Length; t += 3)
		{
			bool inside = true;
			for (int k = 0; k < 3 && inside; k++)
			{
				int i = triangles[t + k];
				Vector3 c = body.Concept[i];
				float x = Mathf.Abs(c.x);
				inside = (body.Part[i] & parts) != 0 && c.y >= y0 && c.y <= y1 && x >= xMin && x <= xMax;
			}
			if (!inside)
			{
				continue;
			}
			float least = float.MaxValue;
			float most = float.MinValue;
			for (int k = 0; k < 3; k++)
			{
				int i = triangles[t + k];
				Vector3 n = body.Normals[i].normalized;
				points[k] = body.Vertices[i] + n * push;
				normals[k] = n;
				weights[k] = body.Weights[i];
				Vector3 c = body.ToConcept.MultiplyPoint3x4(points[k]);
				uvs[k] = new Vector2(Mathf.Atan2(c.x, c.z) / (2f * Mathf.PI) * span, c.y * uv * 2f);
				least = Mathf.Min(least, uvs[k].x);
				most = Mathf.Max(most, uvs[k].x);
			}
			// Keep the triangles across the seam at the back in one piece.
			if (uv > 0f && most - least > span / 2f)
			{
				for (int k = 0; k < 3; k++)
				{
					if (uvs[k].x < 0f)
					{
						uvs[k].x += span;
					}
				}
			}
			mesh.Triangle(material, points, normals, uvs, weights);
		}
	}

	// The front of the torso's surface at x, y (concept units), and the bone weights there for a piece put on it.
	private static float Front(Body body, float x, float y, out BoneWeight weight)
	{
		weight = default;
		for (float window = 0.05f; window < 0.6f; window *= 2f)
		{
			float best = float.MinValue;
			int found = -1;
			for (int i = 0; i < body.Concept.Length; i++)
			{
				Vector3 c = body.Concept[i];
				if (body.Part[i] == Torso && Mathf.Abs(c.x - x) <= window && Mathf.Abs(c.y - y) <= window && c.z > best)
				{
					best = c.z;
					found = i;
				}
			}
			if (found >= 0)
			{
				weight = body.Weights[found];
				return best;
			}
		}
		return ConceptHalfDepth + ConceptFrontShift;
	}

	// A box on the front of the body at x, y, standing out by lift over the surface.
	private static void OnFront(ArmourMesh mesh, Body body, Material material, Vector3 size, float x, float y, float lift)
	{
		float z = Front(body, x, y, out BoneWeight weight);
		mesh.Weight = weight;
		mesh.Box(material, body.ToMesh, size, new Vector3(x, y, z + lift + size.z / 2f));
	}

	// A strap of boxes from one point to another on the front of the body, each following the surface.
	private static void Strap(ArmourMesh mesh, Body body, Material material, Vector2 from, Vector2 to, int pieces, float width, float thickness, float lift)
	{
		Vector2 step = (to - from) / pieces;
		bool across = Mathf.Abs(step.x) > Mathf.Abs(step.y);
		float length = step.magnitude * 1.15f;
		Vector3 size = across ? new Vector3(length, width, thickness) : new Vector3(width, length, thickness);
		for (int i = 0; i < pieces; i++)
		{
			Vector2 at = from + step * (i + 0.5f);
			OnFront(mesh, body, material, size, at.x, at.y, lift);
		}
	}

	private static ArmourMesh BuildSuit(Body body, int mail, int gambeson, int plate)
	{
		ArmourMesh mesh = new ArmourMesh();
		const int Arms = Clavicle | UpperArm | Forearm;
		// Each layer over the one under it.
		float outer = 0f;
		if (gambeson > 0)
		{
			// The concept's gambeson: a quilted shirt with long sleeves, a padded collar, rivets.
			float offset = 0.025f;
			Shell(mesh, body, ArmourMaterials.Get(ArmourMaterials.Quilt, gambeson), Torso | Neck | Arms, 0.3f, 1.16f, 0f, 9f, offset, 1.2f);
			Material collar = (gambeson >= UpgradeLines.MaxTier) ? ArmourMaterials.Get(ArmourMaterials.Metal, gambeson) : ArmourMaterials.Get(ArmourMaterials.Cloth, gambeson);
			Shell(mesh, body, collar, Torso | Neck | Clavicle, 1.05f, 1.2f, 0f, 9f, offset + 0.035f, 0f);
			Vector2[] rivets = new Vector2[7]
			{
				new Vector2(-0.2f, 0.95f),
				new Vector2(0.2f, 0.95f),
				new Vector2(0f, 0.8f),
				new Vector2(-0.22f, 0.7f),
				new Vector2(0.22f, 0.7f),
				new Vector2(-0.12f, 0.56f),
				new Vector2(0.12f, 0.56f)
			};
			for (int i = 0; i < 1 + 2 * gambeson && i < rivets.Length; i++)
			{
				OnFront(mesh, body, ArmourMaterials.Get(ArmourMaterials.Metal, gambeson), new Vector3(0.05f, 0.05f, 0.03f), rivets[i].x, rivets[i].y, offset - 0.01f);
			}
			outer = offset;
		}
		if (mail > 0)
		{
			// The concept's chainmail: a ringed shirt with short sleeves (long from steel), a belt with a gold buckle;
			// steel adds a coif, crystal gold trims.
			float offset = (outer > 0f) ? outer + 0.025f : 0.025f;
			int parts = Torso | Clavicle | UpperArm | ((mail >= 2) ? Forearm | Neck : 0);
			Material rings = ArmourMaterials.Get(ArmourMaterials.Rings, mail);
			Shell(mesh, body, rings, parts, 0.34f, 1.12f, 0f, 9f, offset, 2f);
			Shell(mesh, body, ArmourMaterials.Get(ArmourMaterials.Leather, mail), Torso, 0.5f, 0.58f, 0f, 9f, offset + 0.02f, 0f);
			OnFront(mesh, body, ArmourMaterials.Get(ArmourMaterials.Gold, mail), new Vector3(0.12f, 0.1f, 0.03f), 0f, 0.54f, offset + 0.02f);
			if (mail >= 2)
			{
				Shell(mesh, body, rings, Head | Neck | Torso, 1.08f, 1.22f, 0f, 9f, offset + 0.012f, 2f);
			}
			if (mail >= UpgradeLines.MaxTier)
			{
				Material gold = ArmourMaterials.Get(ArmourMaterials.Gold, mail);
				Shell(mesh, body, gold, Torso | Clavicle | Neck, 1.06f, 1.12f, 0f, 9f, offset + 0.015f, 0f);
				Shell(mesh, body, gold, Torso, 0.34f, 0.4f, 0f, 9f, offset + 0.015f, 0f);
			}
			outer = offset;
		}
		if (plate > 0)
		{
			// The concept's warded plate: a cuirass round chest and back, the arms free, a ridge and a strap, the rune;
			// steel adds pauldrons, crystal a gold trim.
			float offset = Mathf.Max(0.06f, outer + 0.03f);
			Material metal = ArmourMaterials.Get(ArmourMaterials.Metal, plate);
			Material dark = ArmourMaterials.Get(ArmourMaterials.Dark, plate);
			Shell(mesh, body, metal, Torso | Neck | Clavicle, 0.46f, 1.12f, 0f, 0.54f, offset, 0f);
			Strap(mesh, body, dark, new Vector2(0f, 0.5f), new Vector2(0f, 1.06f), 7, 0.045f, 0.025f, offset);
			Strap(mesh, body, dark, new Vector2(-0.46f, 0.62f), new Vector2(0.46f, 0.62f), 9, 0.045f, 0.025f, offset);
			if (plate >= 2)
			{
				Shell(mesh, body, metal, Torso | Clavicle | UpperArm, 0.92f, 1.16f, 0.3f, 0.85f, offset + 0.03f, 0f);
			}
			if (plate >= UpgradeLines.MaxTier)
			{
				Shell(mesh, body, ArmourMaterials.Get(ArmourMaterials.Gold, plate), Torso, 0.46f, 0.51f, 0f, 0.56f, offset + 0.01f, 0f);
			}
			float radius = 0.06f + plate * 0.015f;
			float z = Front(body, 0f, 0.85f, out BoneWeight weight);
			mesh.Weight = weight;
			Vector3 rune = new Vector3(0f, 0.85f, z + offset + radius * 0.6f);
			mesh.Octahedron(ArmourMaterials.Get(ArmourMaterials.Rune, plate), body.ToMesh, radius, rune);
			mesh.Octahedron(ArmourMaterials.Get(ArmourMaterials.RuneGlow, plate), body.ToMesh, radius * 1.7f, rune);
		}
		return mesh;
	}

	// ---- helm

	// The concept's great helm, as wide as the head and from just under the head down over its bottom edge, in the
	// head bone's space.
	private static void BuildHelm(Body body, int tier, string name)
	{
		float width = Mathf.Max(0.86f, Mathf.Max(body.HeadMax.x - body.HeadMin.x, body.HeadMax.z - body.HeadMin.z) * 1.06f);
		float scale = width / 3f;
		float bottom = Mathf.Min(1.13f, body.HeadMin.y + 0.03f);
		// The cylinder (2.3 high) has to reach over the top of the head.
		scale = Mathf.Max(scale, (body.HeadMax.y + 0.03f - bottom) / 2.6f);
		Vector3 at = new Vector3((body.HeadMin.x + body.HeadMax.x) / 2f, bottom, (body.HeadMin.z + body.HeadMax.z) / 2f + 0.02f);
		Matrix4x4 place = body.Bindposes[body.Head] * body.ToMesh * Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * scale);
		ArmourMesh mesh = new ArmourMesh();
		Material metal = ArmourMaterials.Get(ArmourMaterials.Metal, tier);
		Material dark = ArmourMaterials.Get(ArmourMaterials.Dark, tier);
		Material black = ArmourMaterials.Get(ArmourMaterials.Black, tier);
		mesh.Cylinder(metal, place, 1.45f, 1.5f, 2.3f, 8, new Vector3(0f, 1.15f, 0f));
		mesh.Cylinder(metal, place, 0.7f, 1.45f, 0.9f, 8, new Vector3(0f, 2.75f, 0f));
		// Visor slit with its middle bar, nose bar, breathing holes either side.
		mesh.Box(black, place, new Vector3(2.4f, 0.22f, 0.4f), new Vector3(0f, 1.55f, 1.3f));
		mesh.Box(dark, place, new Vector3(0.18f, 0.22f, 0.4f), new Vector3(0f, 1.55f, 1.36f));
		mesh.Box(dark, place, new Vector3(0.35f, 1.3f, 0.3f), new Vector3(0f, 0.75f, 1.4f));
		for (int i = 0; i < 3; i++)
		{
			mesh.Box(black, place, new Vector3(0.12f, 0.12f, 0.3f), new Vector3(-0.6f, 0.5f + i * 0.3f, 1.38f));
			mesh.Box(black, place, new Vector3(0.12f, 0.12f, 0.3f), new Vector3(0.6f, 0.5f + i * 0.3f, 1.38f));
		}
		if (tier >= 2)
		{
			Material red = ArmourMaterials.Get(ArmourMaterials.Red, tier);
			for (int i = 0; i < 6; i++)
			{
				mesh.Box(red, place, new Vector3(0.35f, 0.5f, 0.35f), new Vector3(0f, 3.3f + Mathf.Sin(i * 0.5f) * 0.3f, -0.2f - i * 0.32f), new Vector3(i * 0.2f, 0f, 0f));
			}
		}
		if (tier >= UpgradeLines.MaxTier)
		{
			mesh.Cylinder(ArmourMaterials.Get(ArmourMaterials.Gold, tier), place, 1.55f, 1.55f, 0.25f, 8, new Vector3(0f, 2.25f, 0f));
			mesh.Box(metal, place, new Vector3(0.4f, 0.4f, 0.2f), new Vector3(0f, 2.25f, 1.52f), new Vector3(0f, 0f, Mathf.PI / 4f));
		}
		body.Helms[tier] = mesh.Bake(name + " helm " + tier, null);
		body.HelmMaterials[tier] = mesh.Materials;
	}

	// ---- shield

	// The concept's heater shield: the rim (a bigger heater in the tier's metal behind the face), the face (planks
	// with seams for iron, blue with a gold cross for steel and crystal), the boss and the studs.
	private static void BuildShield(ArmourMesh mesh, Matrix4x4 place, int tier)
	{
		Material metal = ArmourMaterials.Get(ArmourMaterials.Metal, tier);
		Material dark = ArmourMaterials.Get(ArmourMaterials.Dark, tier);
		Material gold = ArmourMaterials.Get(ArmourMaterials.Gold, tier);
		float rim = (tier == 1) ? 1.06f : (tier == 2) ? 1.09f : 1.12f;
		// The concept's bevels are folded into the outline and the thickness.
		mesh.Extrude(metal, place, ArmourMesh.Heater(rim + 0.03f), -0.26f, 0.16f);
		mesh.Extrude((tier == 1) ? ArmourMaterials.Get(ArmourMaterials.Plank, tier) : ArmourMaterials.Get(ArmourMaterials.Blue, tier), place, ArmourMesh.Heater(1.02f), -0.16f, 0.28f);
		if (tier == 1)
		{
			// Plank seams, each cut where the curved bottom is at its x.
			for (int i = -1; i <= 1; i++)
			{
				float x = i * 1.3f;
				float u = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Abs(x) / 2f));
				float low = 0.4f * (1f - u) * (1f - u) - 4f * u * (1f - u) - 3.1f * u * u + 0.25f;
				const float High = 2.3f;
				mesh.Box(ArmourMaterials.Get(ArmourMaterials.WoodDark, tier), place, new Vector3(0.06f, High - low, 0.04f), new Vector3(x, (High + low) / 2f, 0.3f));
			}
		}
		else
		{
			mesh.Box(gold, place, new Vector3(0.5f, 5f, 0.12f), new Vector3(0f, -0.2f, 0.32f));
			mesh.Box(gold, place, new Vector3(3.9f, 0.5f, 0.12f), new Vector3(0f, 0.9f, 0.32f));
		}
		mesh.Cylinder(metal, place, 0.55f, 0.65f, 0.35f, 8, new Vector3(0f, 0.9f, 0.42f), new Vector3(Mathf.PI / 2f, 0f, 0f));
		Vector2[] studs = new Vector2[7]
		{
			new Vector2(-1.85f, 2.2f),
			new Vector2(1.85f, 2.2f),
			new Vector2(-1.85f, 0.4f),
			new Vector2(1.85f, 0.4f),
			new Vector2(-1.2f, -1.6f),
			new Vector2(1.2f, -1.6f),
			new Vector2(0f, -2.95f)
		};
		foreach (Vector2 stud in studs)
		{
			mesh.Box((tier >= UpgradeLines.MaxTier) ? gold : dark, place, new Vector3(0.22f, 0.22f, 0.2f), new Vector3(stud.x * rim, stud.y * rim, 0.28f));
		}
	}
}

// Gear changes on units whose job stays the same (a pooled soldier reused as the same soldier, a client getting
// the unit's tools): check the armour again.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(Toolbag), "OnEquipTool")]
internal static class ArmourModelsEquipPatch
{
	private static void Postfix(Toolbag __instance)
	{
		ArmourModels.Refresh(__instance.GetComponent<Labor>());
	}
}

// Unequipped gear gets its own model back (the concept helm or shield on it goes), then the armour is checked again.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(Toolbag), "OnUnequipTool")]
internal static class ArmourModelsUnequipPatch
{
	private static void Postfix(Toolbag __instance, GameObject item)
	{
		ArmourHidden.RestoreOn(item);
		ArmourModels.Refresh(__instance.GetComponent<Labor>());
	}
}

// The armour a unit wears (the skinned body armour next to its body, the helm on its head bone, the shield on its
// shield) and the game gear it hides. A unit released to the pool (or killed, which releases it) is disabled: the
// pieces go then and the gear gets its model back, so a reused bricktron or shield never shows stale armour.
internal class ArmourWorn : MonoBehaviour
{
	private readonly List<GameObject> _pieces = new List<GameObject>();

	private readonly List<ArmourHidden> _hidden = new List<ArmourHidden>();

	private int[] _tiers = new int[0];

	private GameObject _helmet;

	private GameObject _shield;

	public bool Shows(int[] tiers, GameObject helmet, GameObject shield)
	{
		if (tiers.Length != _tiers.Length)
		{
			bool none = true;
			foreach (int tier in tiers)
			{
				none &= tier == 0;
			}
			return none && _pieces.Count == 0 && _hidden.Count == 0;
		}
		for (int i = 0; i < tiers.Length; i++)
		{
			if (tiers[i] != _tiers[i])
			{
				return false;
			}
		}
		return helmet == _helmet && shield == _shield;
	}

	public void Remember(int[] tiers, GameObject helmet, GameObject shield)
	{
		_tiers = tiers;
		_helmet = helmet;
		_shield = shield;
	}

	public void Add(GameObject piece)
	{
		_pieces.Add(piece);
	}

	public void Hide(GameObject item)
	{
		_hidden.Add(ArmourHidden.Hide(item));
	}

	public void Clear()
	{
		foreach (GameObject piece in _pieces)
		{
			if (piece != null)
			{
				Destroy(piece);
			}
		}
		_pieces.Clear();
		foreach (ArmourHidden hidden in _hidden)
		{
			if (hidden != null)
			{
				hidden.Restore();
			}
		}
		_hidden.Clear();
		_tiers = new int[0];
		_helmet = null;
		_shield = null;
	}

	private void OnDisable()
	{
		Clear();
	}
}

// A game helmet or shield shown as the concept's: its meshes swapped for an empty one (the fog of war switches its
// renderers on and off, so they cannot simply be switched off), and given back when the armour goes or the gear is
// unequipped.
internal class ArmourHidden : MonoBehaviour
{
	private static Mesh _empty;

	private MeshFilter[] _filters = new MeshFilter[0];

	private Mesh[] _meshes = new Mesh[0];

	private SkinnedMeshRenderer[] _skins = new SkinnedMeshRenderer[0];

	private Mesh[] _skinMeshes = new Mesh[0];

	private bool _hidden;

	internal static Mesh Empty
	{
		get
		{
			if (_empty == null)
			{
				_empty = new Mesh();
				_empty.name = "CastleStoryPlus hidden gear";
			}
			return _empty;
		}
	}

	public static ArmourHidden Hide(GameObject item)
	{
		ArmourHidden hidden = item.GetComponent<ArmourHidden>();
		if (hidden == null)
		{
			hidden = item.AddComponent<ArmourHidden>();
		}
		hidden.Restore();
		List<MeshFilter> filters = new List<MeshFilter>();
		foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(includeInactive: true))
		{
			if (filter.sharedMesh != null && !filter.name.StartsWith("CastleStoryPlus", StringComparison.Ordinal))
			{
				filters.Add(filter);
			}
		}
		hidden._filters = filters.ToArray();
		hidden._meshes = new Mesh[hidden._filters.Length];
		for (int i = 0; i < hidden._filters.Length; i++)
		{
			hidden._meshes[i] = hidden._filters[i].sharedMesh;
			hidden._filters[i].sharedMesh = Empty;
		}
		hidden._skins = item.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		hidden._skinMeshes = new Mesh[hidden._skins.Length];
		for (int i = 0; i < hidden._skins.Length; i++)
		{
			hidden._skinMeshes[i] = hidden._skins[i].sharedMesh;
			hidden._skins[i].sharedMesh = Empty;
		}
		hidden._hidden = true;
		return hidden;
	}

	public static void RestoreOn(GameObject item)
	{
		ArmourHidden hidden = (item != null) ? item.GetComponent<ArmourHidden>() : null;
		if (hidden != null)
		{
			hidden.Restore();
		}
	}

	// The gear's own meshes back, and the concept pieces put on it gone.
	public void Restore()
	{
		if (!_hidden)
		{
			return;
		}
		_hidden = false;
		for (int i = 0; i < _filters.Length; i++)
		{
			if (_filters[i] != null && _filters[i].sharedMesh == Empty)
			{
				_filters[i].sharedMesh = _meshes[i];
			}
		}
		for (int i = 0; i < _skins.Length; i++)
		{
			if (_skins[i] != null && _skins[i].sharedMesh == Empty)
			{
				_skins[i].sharedMesh = _skinMeshes[i];
			}
		}
		foreach (Transform child in GetComponentsInChildren<Transform>(includeInactive: true))
		{
			if (child != null && child.name.StartsWith("CastleStoryPlus armour", StringComparison.Ordinal))
			{
				Destroy(child.gameObject);
			}
		}
	}
}
