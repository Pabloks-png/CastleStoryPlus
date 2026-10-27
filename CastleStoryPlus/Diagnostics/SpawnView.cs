using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.Beacons;
using Brix.Input;
using Brix.Lua;
using CastleStoryPlus.Invasion;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CastleStoryPlus.Diagnostics;

// Debug menu "Spawn points": shows the map's corruptron spawn points (invasion) as tall pillars with a label (number,
// blocks to the nearest built block, chance to be drawn by SpawnDistance): green may be drawn, red is too close to
// the players' blocks, yellow was drawn last. A left click on a pillar picks it up (white), the next left click on the
// ground puts it there; a right click or Esc drops it where it was. The map's marker itself is moved, so the next
// waves come from the new place and it is saved with the world. Moving is for the host only.
internal class SpawnView : MonoBehaviour
{
	internal static readonly Color ClearColor = new Color(0.3f, 1f, 0.35f, 0.75f);

	internal static readonly Color BlockedColor = new Color(1f, 0.3f, 0.25f, 0.75f);

	internal static readonly Color LastColor = new Color(1f, 0.85f, 0.2f, 0.85f);

	private static readonly Color HeldColor = new Color(1f, 1f, 1f, 0.9f);

	private const string Group = "Corruptron Spawn Point";

	private const float PillarHeight = 60f;

	private const float PillarWidth = 1.5f;

	// How near (pixels) a click has to be to a pillar's foot on the screen to pick it up.
	private const float PickPixels = 40f;

	private const float RefreshSeconds = 1f;

	internal static bool Enabled;

	internal static string Summary = string.Empty;

	private static SpawnView _instance;

	private static Material _material;

	private readonly List<LineRenderer> _pillars = new List<LineRenderer>();

	private readonly List<Text> _labels = new List<Text>();

	private readonly List<LuaMarker> _markers = new List<LuaMarker>();

	private readonly List<object[]> _rows = new List<object[]>();

	private RectTransform _canvas;

	private GameObject _hint;

	private LuaMarker _held;

	private int _heldFrame;

	private float _nextRefresh;

	internal static void Toggle()
	{
		Enabled = !Enabled;
		if (_instance == null)
		{
			_instance = Plugin.Root.AddComponent<SpawnView>();
		}
		_instance._nextRefresh = 0f;
		if (!Enabled)
		{
			_instance.Drop();
			_instance.HideAll(0);
			Summary = string.Empty;
		}
	}

	private void Update()
	{
		if (!Enabled)
		{
			return;
		}
		if (Time.unscaledTime >= _nextRefresh)
		{
			_nextRefresh = Time.unscaledTime + RefreshSeconds;
			Refresh();
		}
		HandleMouse();
	}

	private void LateUpdate()
	{
		if (Enabled)
		{
			Draw();
		}
	}

	private void Refresh()
	{
		_markers.Clear();
		_rows.Clear();
		try
		{
			_markers.AddRange(LuaMarker.GetGroup(Group));
			_rows.AddRange(SpawnDistance.Describe());
		}
		catch (System.Exception ex)
		{
			Summary = "No spawn points: " + ex.Message;
			_markers.Clear();
			_rows.Clear();
			return;
		}
		int clear = 0;
		foreach (object[] row in _rows)
		{
			if ((bool)row[2])
			{
				clear++;
			}
		}
		Summary = (_markers.Count == 0)
			? "This map has no corruptron spawn points."
			: _markers.Count + " spawn points, " + clear + " far enough (" + SpawnDistance.MinBlocks + " blocks). Click a pillar, then the ground, to move it.";
	}

	private void Draw()
	{
		Camera camera = Camera.main;
		Vector2? last = SpawnDistance.LastPicked;
		int count = Mathf.Min(_markers.Count, _rows.Count);
		for (int i = 0; i < count; i++)
		{
			LuaMarker marker = _markers[i];
			if (marker == null)
			{
				continue;
			}
			Vector3 at = marker.position;
			bool clear = (bool)_rows[i][2];
			bool isLast = last.HasValue && Mathf.Abs(last.Value.x - at.x) < 0.5f && Mathf.Abs(last.Value.y - at.z) < 0.5f;
			Color color = (marker == _held) ? HeldColor : isLast ? LastColor : clear ? ClearColor : BlockedColor;
			LineRenderer pillar = PillarAt(i);
			pillar.enabled = true;
			pillar.startColor = color;
			pillar.endColor = new Color(color.r, color.g, color.b, 0.1f);
			pillar.SetPosition(0, at);
			pillar.SetPosition(1, at + Vector3.up * PillarHeight);
			Text label = LabelAt(i);
			Vector3 screen = (camera != null) ? camera.WorldToScreenPoint(at + Vector3.up * 3f) : Vector3.back;
			bool onScreen = screen.z > 0f;
			label.gameObject.SetActive(onScreen);
			if (onScreen)
			{
				float nearest = (float)_rows[i][1];
				double chance = (double)_rows[i][3];
				label.text = "Spawn " + (i + 1) + "\n" + ((nearest >= 99999f) ? "nothing built" : (Mathf.RoundToInt(nearest) + " blocks")) + ", " + Mathf.RoundToInt((float)(chance * 100.0)) + "%";
				label.color = new Color(color.r, color.g, color.b, 1f);
				label.transform.parent.position = new Vector3(screen.x, screen.y, 0f);
			}
		}
		HideAll(count);
	}

	private void HandleMouse()
	{
		if (_held != null)
		{
			if (Time.frameCount == _heldFrame)
			{
				return;
			}
			if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
			{
				Drop();
				return;
			}
			if (Input.GetMouseButtonDown(0) && !OverUI())
			{
				if (TryGround(Input.mousePosition, out Vector3 position))
				{
					Move(_held, position);
				}
				Drop();
			}
			return;
		}
		if (!Input.GetMouseButtonDown(0) || OverUI() || !NetworkServer.active)
		{
			return;
		}
		Camera camera = Camera.main;
		if (camera == null)
		{
			return;
		}
		LuaMarker best = null;
		float bestPixels = PickPixels;
		foreach (LuaMarker marker in _markers)
		{
			if (marker == null)
			{
				continue;
			}
			// The pillar is picked anywhere along its lower part, not only at its foot.
			for (float up = 0f; up <= 12f; up += 3f)
			{
				Vector3 screen = camera.WorldToScreenPoint(marker.position + Vector3.up * up);
				if (screen.z <= 0f)
				{
					continue;
				}
				float pixels = Vector2.Distance(new Vector2(screen.x, screen.y), Input.mousePosition);
				if (pixels < bestPixels)
				{
					best = marker;
					bestPixels = pixels;
				}
			}
		}
		if (best != null)
		{
			Hold(best);
		}
	}

	private void Hold(LuaMarker marker)
	{
		_held = marker;
		_heldFrame = Time.frameCount;
		if (Picking.Instance != null)
		{
			Picking.Instance.UILocked = true;
		}
		EnsureCanvas();
		_hint.SetActive(true);
	}

	private void Drop()
	{
		if (_held == null)
		{
			return;
		}
		_held = null;
		if (Picking.Instance != null)
		{
			Picking.Instance.UILocked = false;
		}
		if (_hint != null)
		{
			_hint.SetActive(false);
		}
	}

	// The marker itself (what the invasion script reads for every wave) and its beacon, which carries the position
	// over the network and into the save.
	private void Move(LuaMarker marker, Vector3 position)
	{
		Vector3 from = marker.position;
		marker.position = position;
		BeaconDriver driver = marker.GetComponent<BeaconDriver>();
		if (driver != null)
		{
			driver.NetworkNetworkPosition = position;
		}
		Plugin.Log.LogInfo("SpawnView: spawn point moved from " + Mathf.RoundToInt(from.x) + "," + Mathf.RoundToInt(from.y) + "," + Mathf.RoundToInt(from.z) + " to " + Mathf.RoundToInt(position.x) + "," + Mathf.RoundToInt(position.y) + "," + Mathf.RoundToInt(position.z));
		Refresh();
	}

	private static bool TryGround(Vector2 mouse, out Vector3 position)
	{
		position = Vector3.zero;
		if (Camera.main == null || Picking.Instance == null)
		{
			return false;
		}
		VoxelRaycastHit hit = new VoxelRaycastHit();
		LayerBundle layers = new LayerBundle((int)UnityLayer.Terrain);
		if (!hit.PlaceAtRaycast(Camera.main.ScreenPointToRay(mouse), Picking.Instance.MaxDistance, layers))
		{
			return false;
		}
		XYZ voxel = hit.VoxelPosition;
		if (!Voxel.IsOpenAndWalkable(voxel))
		{
			voxel = voxel + XYZ.FromVector3(Vector3.up);
		}
		position = voxel.ToVector3();
		return true;
	}

	private static bool OverUI()
	{
		return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
	}

	private LineRenderer PillarAt(int index)
	{
		while (_pillars.Count <= index)
		{
			GameObject go = new GameObject("CastleStoryPlusSpawnPillar");
			go.transform.SetParent(transform, false);
			LineRenderer line = go.AddComponent<LineRenderer>();
			if (_material == null)
			{
				Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
				_material = new Material(shader);
			}
			line.sharedMaterial = _material;
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.startWidth = PillarWidth;
			line.endWidth = PillarWidth;
			_pillars.Add(line);
		}
		return _pillars[index];
	}

	private Text LabelAt(int index)
	{
		while (_labels.Count <= index)
		{
			EnsureCanvas();
			RectTransform rect = UiKit.CreateRect("SpawnLabel", _canvas);
			rect.sizeDelta = new Vector2(220f, 44f);
			Text text = UiKit.CreateText(rect, string.Empty, 15, Color.white, TextAnchor.LowerCenter);
			text.fontStyle = FontStyle.Bold;
			text.raycastTarget = false;
			UiKit.SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Outline outline = text.gameObject.AddComponent<Outline>();
			outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
			outline.effectDistance = new Vector2(1.5f, -1.5f);
			_labels.Add(text);
		}
		return _labels[index];
	}

	private void HideAll(int from)
	{
		for (int i = from; i < _pillars.Count; i++)
		{
			_pillars[i].enabled = false;
		}
		for (int i = from; i < _labels.Count; i++)
		{
			_labels[i].gameObject.SetActive(false);
		}
	}

	private void EnsureCanvas()
	{
		if (_canvas != null)
		{
			return;
		}
		_canvas = UiKit.CreateCanvas("CastleStoryPlusSpawnView", transform, 900).GetComponent<RectTransform>();
		RectTransform hint = UiKit.CreateRect("Hint", _canvas);
		UiKit.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 40f));
		Text text = UiKit.CreateText(hint, "Left click the ground to move the spawn point there. Right click or Esc cancels.", 20, Color.white, TextAnchor.MiddleCenter);
		text.fontStyle = FontStyle.Bold;
		UiKit.SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
		_hint = hint.gameObject;
		_hint.SetActive(false);
	}
}
