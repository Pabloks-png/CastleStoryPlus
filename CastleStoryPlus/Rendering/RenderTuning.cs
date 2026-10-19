using System.Collections.Generic;
using BepInEx.Configuration;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleStoryPlus.Rendering;

// More frames per second for the same picture. The camera already leaves out what is outside the screen, but the
// sun's shadow maps were drawn out to 400 blocks in 4 cascades, and drew every tree and piece of terrain in that
// range again, most of them far outside the screen (at 2560 x 1440 that was about half of all vertices drawn).
// - [RenderTuning] ShadowDistance (150) and ShadowCascades (2) replace the quality level's 400 and 4; kept while the
//   game runs, also after the graphics options change the quality level. 0 leaves the game's own value.
// - OffscreenShadows: a tree or piece of terrain only casts a shadow when the shadow can land on the screen: its
//   bounds, stretched ShadowReach blocks along the sun's light and grown by Margin, touch the main camera's view.
//   Checked every frame against bounds read every ReachSeconds (trees and terrain stand still, and reading a
//   renderer's bounds is a call into Unity); new renderers (rebuilt terrain) are looked up every RescanSeconds.
[Feature(Features.RenderTuning, Features.RenderTuningInfo)]
internal class RenderTuning : MonoBehaviour
{
	private static readonly string[] CasterLayers = new string[2] { "Nature", "Terrain" };

	private const float RescanSeconds = 10f;

	private const float LightSeconds = 5f;

	// How often the stretched bounds follow the sun (it moves slowly over the day).
	private const float ReachSeconds = 2f;

	internal static ConfigEntry<float> ShadowDistance;

	internal static ConfigEntry<int> ShadowCascades;

	internal static ConfigEntry<bool> OffscreenShadows;

	internal static ConfigEntry<float> ShadowReach;

	internal static ConfigEntry<float> Margin;

	private sealed class Caster
	{
		internal Renderer Renderer;

		// The shadow casting it had before.
		internal ShadowCastingMode Original;

		// Its bounds stretched along the sun's light and grown by the margin.
		internal Vector3 Center;

		internal Vector3 Extents;

		internal bool Off;
	}

	private static readonly List<Caster> Casters = new List<Caster>();

	private static readonly HashSet<Renderer> Known = new HashSet<Renderer>();

	private static readonly Plane[] Planes = new Plane[6];

	// Where the trees and terrain were found, looked through again for new ones.
	private static readonly HashSet<Transform> Folders = new HashSet<Transform>();

	private static readonly List<Renderer> Found = new List<Renderer>();

	internal static int Managed => Casters.Count;

	// Of the managed renderers, how many cast no shadow last frame.
	internal static int CastersOff { get; private set; }

	private Light _sun;

	// The light used for the sun's direction, for the developer API.
	internal static Light Sun { get; private set; }

	private float _nextRescan;

	private float _nextLight;

	private float _nextReach;

	private int _layerMask;

	private static void Enable()
	{
		ShadowDistance = Plugin.Cfg.Bind("RenderTuning", "ShadowDistance", 150f, new ConfigDescription("How far from the camera shadows are drawn, in blocks (the game's quality level uses 400). 0 keeps the game's value.", new AcceptableValueRange<float>(0f, 1000f)));
		ShadowCascades = Plugin.Cfg.Bind("RenderTuning", "ShadowCascades", 2, new ConfigDescription("Shadow cascades: 1 (none), 2 or 4 (the game's quality level uses 4; more is sharper near the camera and costs a drawing of the shadow casters each). 0 keeps the game's value.", new AcceptableValueList<int>(0, 1, 2, 4)));
		OffscreenShadows = Plugin.Cfg.Bind("RenderTuning", "OffscreenShadows", true, "Trees and terrain only cast a shadow when it can land on the screen, instead of every one within the shadow distance being drawn into the shadow maps.");
		ShadowReach = Plugin.Cfg.Bind("RenderTuning", "ShadowReach", 40f, new ConfigDescription("How far, in blocks along the sun's light, a tree or piece of terrain outside the screen may be and still cast a shadow onto it. Too short: long shadows at sunrise and sunset end at the screen's edge.", new AcceptableValueRange<float>(0f, 400f)));
		Margin = Plugin.Cfg.Bind("RenderTuning", "Margin", 4f, new ConfigDescription("Extra blocks round each tree and piece of terrain for that test, so shadows do not pop in when the camera turns fast.", new AcceptableValueRange<float>(0f, 50f)));
		OffscreenShadows.SettingChanged += (sender, args) =>
		{
			if (!OffscreenShadows.Value)
			{
				RestoreAll();
			}
		};
		Plugin.Root.AddComponent<RenderTuning>();
	}

	private void Update()
	{
		if (ShadowDistance.Value > 0f && !Mathf.Approximately(QualitySettings.shadowDistance, ShadowDistance.Value))
		{
			QualitySettings.shadowDistance = ShadowDistance.Value;
		}
		if (ShadowCascades.Value > 0 && QualitySettings.shadowCascades != ShadowCascades.Value)
		{
			QualitySettings.shadowCascades = ShadowCascades.Value;
		}
	}

	private void LateUpdate()
	{
		if (!OffscreenShadows.Value)
		{
			return;
		}
		Camera camera = Camera.main;
		if (camera == null || QualitySettings.shadows == ShadowQuality.Disable)
		{
			RestoreAll();
			return;
		}
		float now = Time.realtimeSinceStartup;
		if (now >= _nextLight)
		{
			_nextLight = now + LightSeconds;
			Light sun = FindSun();
			if (sun != _sun)
			{
				// The stretched bounds follow the new light at once.
				_nextReach = 0f;
			}
			_sun = sun;
			Sun = sun;
		}
		if (_sun == null)
		{
			// At night no light casts a shadow, so no shadow map is drawn: nothing to leave out.
			return;
		}
		bool rescan = now >= _nextRescan;
		if (rescan)
		{
			_nextRescan = now + RescanSeconds;
			Rescan();
		}
		if (rescan || now >= _nextReach)
		{
			_nextReach = now + ReachSeconds;
			UpdateReach();
		}
		SetPlanes(camera.projectionMatrix * camera.worldToCameraMatrix);
		int off = 0;
		bool gone = false;
		for (int i = 0; i < Casters.Count; i++)
		{
			Caster caster = Casters[i];
			bool cast = InView(caster.Center, caster.Extents);
			if (cast == caster.Off)
			{
				if (caster.Renderer == null)
				{
					gone = true;
					continue;
				}
				caster.Off = !cast;
				caster.Renderer.shadowCastingMode = cast ? caster.Original : ShadowCastingMode.Off;
			}
			if (caster.Off)
			{
				off++;
			}
		}
		CastersOff = off;
		if (gone)
		{
			RemoveGone();
		}
	}

	// The view's six planes, facing inwards, from the camera's view-projection matrix (Gribb and Hartmann); Unity 5.6
	// only has a version that makes a new array every call.
	private static void SetPlanes(Matrix4x4 m)
	{
		SetPlane(0, m.m30 + m.m00, m.m31 + m.m01, m.m32 + m.m02, m.m33 + m.m03);
		SetPlane(1, m.m30 - m.m00, m.m31 - m.m01, m.m32 - m.m02, m.m33 - m.m03);
		SetPlane(2, m.m30 + m.m10, m.m31 + m.m11, m.m32 + m.m12, m.m33 + m.m13);
		SetPlane(3, m.m30 - m.m10, m.m31 - m.m11, m.m32 - m.m12, m.m33 - m.m13);
		SetPlane(4, m.m30 + m.m20, m.m31 + m.m21, m.m32 + m.m22, m.m33 + m.m23);
		SetPlane(5, m.m30 - m.m20, m.m31 - m.m21, m.m32 - m.m22, m.m33 - m.m23);
	}

	private static void SetPlane(int i, float x, float y, float z, float w)
	{
		float length = Mathf.Sqrt(x * x + y * y + z * z);
		Planes[i].normal = new Vector3(x, y, z) / length;
		Planes[i].distance = w / length;
	}

	// A box touches the view unless it lies wholly behind one of its planes (the planes face inwards).
	private static bool InView(Vector3 center, Vector3 extents)
	{
		for (int i = 0; i < Planes.Length; i++)
		{
			Vector3 n = Planes[i].normal;
			float r = extents.x * Mathf.Abs(n.x) + extents.y * Mathf.Abs(n.y) + extents.z * Mathf.Abs(n.z);
			if (n.x * center.x + n.y * center.y + n.z * center.z + Planes[i].distance + r < 0f)
			{
				return false;
			}
		}
		return true;
	}

	// Trees and terrain stand still: their bounds are read here, not every frame.
	private void UpdateReach()
	{
		Vector3 light = (_sun != null) ? _sun.transform.forward * ShadowReach.Value : Vector3.zero;
		float grow = Margin.Value * 2f;
		bool gone = false;
		for (int i = 0; i < Casters.Count; i++)
		{
			Caster caster = Casters[i];
			if (caster.Renderer == null)
			{
				gone = true;
				continue;
			}
			Bounds bounds = caster.Renderer.bounds;
			Bounds reach = bounds;
			reach.Encapsulate(new Bounds(bounds.center + light, bounds.size));
			reach.Expand(grow);
			caster.Center = reach.center;
			caster.Extents = reach.extents;
		}
		if (gone)
		{
			RemoveGone();
		}
	}

	// The directional light with shadows, the brightest if there are several.
	private static Light FindSun()
	{
		Light best = null;
		foreach (Light light in FindObjectsOfType<Light>())
		{
			if (light.type == LightType.Directional && light.shadows != LightShadows.None && light.isActiveAndEnabled && (best == null || light.intensity > best.intensity))
			{
				best = light;
			}
		}
		return best;
	}

	private void Rescan()
	{
		if (_layerMask == 0)
		{
			foreach (string name in CasterLayers)
			{
				int layer = LayerMask.NameToLayer(name);
				if (layer >= 0)
				{
					_layerMask |= 1 << layer;
				}
			}
		}
		// Looking through every object of the scene takes 25 ms, a stutter: once one is found, only the folders the
		// trees and terrain were found in are looked through again (the game keeps them under Jeu/MegaVoxels).
		if (Folders.Count == 0)
		{
			foreach (Renderer renderer in FindObjectsOfType<Renderer>())
			{
				if (Take(renderer))
				{
					Folders.Add(Folder(renderer.transform));
				}
			}
			return;
		}
		Folders.RemoveWhere((Transform folder) => folder == null);
		foreach (Transform folder in Folders)
		{
			folder.GetComponentsInChildren(false, Found);
			foreach (Renderer renderer in Found)
			{
				Take(renderer);
			}
			Found.Clear();
		}
	}

	// Manages a tree or piece of terrain that casts a shadow and is not managed yet.
	private bool Take(Renderer renderer)
	{
		if ((_layerMask & (1 << renderer.gameObject.layer)) == 0 || Known.Contains(renderer))
		{
			return false;
		}
		ShadowCastingMode mode = renderer.shadowCastingMode;
		if (mode != ShadowCastingMode.On && mode != ShadowCastingMode.TwoSided)
		{
			return false;
		}
		Known.Add(renderer);
		Casters.Add(new Caster { Renderer = renderer, Original = mode });
		return true;
	}

	// The child of the scene's top object that holds it (Jeu/MegaVoxels), or the object itself at the top.
	private static Transform Folder(Transform t)
	{
		while (t.parent != null && t.parent.parent != null)
		{
			t = t.parent;
		}
		return t;
	}

	private static void RemoveGone()
	{
		Casters.RemoveAll((Caster caster) => caster.Renderer == null);
		Known.RemoveWhere((Renderer renderer) => renderer == null);
	}

	private static void RestoreAll()
	{
		foreach (Caster caster in Casters)
		{
			if (caster.Off && caster.Renderer != null)
			{
				caster.Renderer.shadowCastingMode = caster.Original;
			}
		}
		Casters.Clear();
		Known.Clear();
		Folders.Clear();
		CastersOff = 0;
	}
}
