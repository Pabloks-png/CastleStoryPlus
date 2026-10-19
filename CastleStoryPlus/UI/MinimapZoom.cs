using BepInEx.Configuration;
using Brix.Engine;
using Brix.Input;
using Brix.UI;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleStoryPlus.UI;

// Zoom on the minimap: the mouse wheel over it zooms in and out in steps (1x is the whole island, as the game draws
// it, up to 6x), and a zoomed map is centred on the camera and follows it, held inside the island's edges. The
// map picture is panned through the RawImage's uvRect, so nothing is drawn again; the markers (units, crystals,
// the camera) are placed in the zoomed view, those outside it hidden, and a click on the map moves the camera to
// the place under the mouse at every zoom. The wheel over the minimap no longer zooms the game camera. The zoom is
// kept in the config ([Minimap] Zoom).
//
// Also for every zoom: MiniMap read its own size only once, when the level was ready, and placed the markers by
// it; a minimap built later (the C# scoreboard) or resized afterwards had its markers squeezed into a corner. The
// size is now read each time a marker is placed.
[Feature(Features.MinimapZoom, Features.MinimapZoomInfo)]
[HarmonyPatch]
internal class MinimapZoom : MonoBehaviour
{
	private static readonly float[] Steps = { 1f, 1.5f, 2f, 3f, 4f, 6f };

	// Markers placed again at most this often while the zoomed view moves (they are otherwise placed once a second).
	private const float MarkerSeconds = 0.1f;

	private static ConfigEntry<float> _zoom;

	private static MinimapZoom _current;

	private MiniMap _minimap;

	private RectTransform _rect;

	private Canvas _canvas;

	// The part of the map shown, in map coordinates (0..1 over the minimap's bounds).
	private Rect _view = new Rect(0f, 0f, 1f, 1f);

	private bool _markersDirty;

	private float _nextMarkers;

	private static void Enable()
	{
		_zoom = Plugin.Cfg.Bind("Minimap", "Zoom", 1f, "Minimap zoom (1 = the whole island; 1.5, 2, 3, 4 or 6). Changed with the mouse wheel over the minimap.");
	}

	private static int StepIndex()
	{
		float zoom = (_zoom != null) ? _zoom.Value : 1f;
		int best = 0;
		for (int i = 1; i < Steps.Length; i++)
		{
			if (Mathf.Abs(Steps[i] - zoom) < Mathf.Abs(Steps[best] - zoom))
			{
				best = i;
			}
		}
		return best;
	}

	private static float Zoom
	{
		get { return Steps[StepIndex()]; }
	}

	// The shown part of this minimap's map; all of it when the minimap has no zoom (yet).
	private static Rect ViewOf(MiniMap minimap)
	{
		return (_current != null && _current._minimap == minimap) ? _current._view : new Rect(0f, 0f, 1f, 1f);
	}

	// Whether the mouse is over a zoomable minimap (the game's own hover flag, set while the minimap is shown and
	// takes clicks, plus the minimap's rect, in case there are several).
	private static bool Hovered
	{
		get { return _current != null && _current.IsHovered(); }
	}

	[HarmonyPatch(typeof(MiniMap), nameof(MiniMap.InitCameras))]
	[HarmonyPostfix]
	private static void Attach(MiniMap __instance)
	{
		if (__instance.GetComponent<MinimapZoom>() != null)
		{
			return;
		}
		MinimapZoom zoom = __instance.gameObject.AddComponent<MinimapZoom>();
		zoom._minimap = __instance;
		zoom._rect = __instance.transform as RectTransform;
		_current = zoom;
	}

	// The game's SetTargetPosition, with the size read each time and the position mapped into the shown part.
	[HarmonyPatch(typeof(MiniMap), nameof(MiniMap.SetTargetPosition))]
	[HarmonyPrefix]
	private static bool Place(MiniMap __instance, Transform trs, Transform targetTrs, bool clampPosition)
	{
		RectTransform rect = __instance.transform as RectTransform;
		if (rect != null)
		{
			__instance.size = rect.rect.size;
		}
		Rect view = ViewOf(__instance);
		Bounds bounds = __instance.mapBounds;
		Vector3 position = trs.position;
		Vector2 point = new Vector2((position.x - bounds.min.x) / bounds.size.x, (position.z - bounds.min.z) / bounds.size.z);
		point = new Vector2((point.x - view.xMin) / view.width, (point.y - view.yMin) / view.height);
		bool inside = point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
		bool shown;
		if (clampPosition && view.width >= 1f)
		{
			// The whole island: as the game, things outside the bounds are held at the edge.
			point = new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
			shown = true;
		}
		else
		{
			shown = inside;
		}
		if (targetTrs.gameObject.activeSelf != shown)
		{
			targetTrs.gameObject.SetActive(shown);
		}
		targetTrs.localPosition = new Vector3(point.x * __instance.size.x, point.y * __instance.size.y, 0f);
		return false;
	}

	// A click or drag on the map: the world position under the mouse in the shown part.
	[HarmonyPatch(typeof(MiniMap), nameof(MiniMap.GetSurfaceAt))]
	[HarmonyPrefix]
	private static bool SurfaceAt(MiniMap __instance, PointerEventData data, ref Vector3 __result)
	{
		Rect view = ViewOf(__instance);
		RectTransform rect = __instance.transform as RectTransform;
		if (view.width >= 1f || rect == null)
		{
			return true;
		}
		RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 local);
		Vector2 size = rect.rect.size;
		Vector2 point = new Vector2(Mathf.Clamp01(local.x / size.x + rect.pivot.x), Mathf.Clamp01(local.y / size.y + rect.pivot.y));
		point = new Vector2(view.xMin + point.x * view.width, view.yMin + point.y * view.height);
		Bounds bounds = __instance.mapBounds;
		Vector3 position = new Vector3(point.x * bounds.size.x + bounds.min.x, CameraScript.Target.y, point.y * bounds.size.z + bounds.min.z);
		__result = Architecte.SurfaceRaycast(position, 50);
		return false;
	}

	// The wheel over the minimap zooms the minimap, not the camera: what the camera read from it this frame is undone.
	[HarmonyPatch(typeof(CameraScript), nameof(CameraScript.GetMovementVectorsFromInputs))]
	[HarmonyPrefix]
	private static void BeforeCameraInput(CameraScript __instance, out Vector2 __state)
	{
		__state = new Vector2(__instance._zoomInput, __instance._climbSpeed);
	}

	[HarmonyPatch(typeof(CameraScript), nameof(CameraScript.GetMovementVectorsFromInputs))]
	[HarmonyPostfix]
	private static void AfterCameraInput(CameraScript __instance, Vector2 __state)
	{
		if (Input.mouseScrollDelta.y != 0f && Hovered)
		{
			__instance._zoomInput = __state.x;
			__instance._climbSpeed = __state.y;
		}
	}

	private bool IsHovered()
	{
		if (_rect == null || !CursorPicker.IsHoveringMinimap || !isActiveAndEnabled)
		{
			return false;
		}
		if (_canvas == null)
		{
			_canvas = GetComponentInParent<Canvas>();
		}
		Camera camera = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;
		return RectTransformUtility.RectangleContainsScreenPoint(_rect, Input.mousePosition, camera);
	}

	private void Update()
	{
		float wheel = Input.mouseScrollDelta.y;
		if (wheel != 0f && IsHovered())
		{
			int step = Mathf.Clamp(StepIndex() + ((wheel > 0f) ? 1 : -1), 0, Steps.Length - 1);
			if (_zoom != null && _zoom.Value != Steps[step])
			{
				_zoom.Value = Steps[step];
			}
		}
	}

	private void LateUpdate()
	{
		if (_minimap == null || _minimap.camTarget == null)
		{
			return;
		}
		Rect view = ViewFor(Zoom);
		if (view != _view)
		{
			float pixels = Mathf.Max(Mathf.Abs(view.xMin - _view.xMin), Mathf.Abs(view.yMin - _view.yMin), Mathf.Abs(view.width - _view.width)) / view.width * Mathf.Max(_minimap.size.x, 1f);
			// Under half a pixel of the minimap: not worth placing the markers again.
			if (pixels >= 0.5f || view.width != _view.width)
			{
				_view = view;
				_markersDirty = true;
				_minimap.UpdateCameras();
			}
		}
		RawImage image = _minimap.mapImage;
		// Set again when MinimapTerrain swaps in its picture (it resets the uvRect).
		if (image != null && image.uvRect != _view)
		{
			image.uvRect = _view;
		}
		if (_markersDirty && Time.unscaledTime >= _nextMarkers)
		{
			_markersDirty = false;
			_nextMarkers = Time.unscaledTime + MarkerSeconds;
			_minimap.UpdateBricktrons();
			_minimap.UpdateCrystals();
			_minimap.UpdateTrackables();
		}
	}

	// The shown part at this zoom: centred on the camera's target, held inside the map.
	private Rect ViewFor(float zoom)
	{
		if (zoom <= 1f)
		{
			return new Rect(0f, 0f, 1f, 1f);
		}
		Bounds bounds = _minimap.mapBounds;
		Vector3 target = _minimap.camTarget.position;
		float half = 0.5f / zoom;
		float x = Mathf.Clamp((target.x - bounds.min.x) / bounds.size.x, half, 1f - half);
		float y = Mathf.Clamp((target.z - bounds.min.z) / bounds.size.z, half, 1f - half);
		return new Rect(x - half, y - half, half * 2f, half * 2f);
	}

	private void OnDestroy()
	{
		if (_current == this)
		{
			_current = null;
		}
	}
}
