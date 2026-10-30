using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Input;
using Brix.Legacy;
using Brix.UI.GameSelector;
using Brix.Utils;
using CastleStoryPlus.Core;
using CastleStoryPlus.WorkerAI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using static CastleStoryPlus.UI.UiKit;

namespace CastleStoryPlus.Building;

// Hovering a blueprint that is not being built says why, in a small panel over the cursor (next to the game's list
// of what it needs): its task is not committed, paused or has no workers, something is in the way, it waits for what
// it rests on, or no bricktron can get to a place to build it from (with how many of the task's blueprints are stuck
// so). What is in the way is named, and its cells are marked red in the world (the game's own obstruction check per
// cell). Nothing shows while the blueprint can be built. Whether bricktrons can reach it is known on the host only.
[Feature(Features.BuildBlocked, Features.BuildBlockedInfo)]
internal sealed class BuildBlocked : MonoBehaviour
{
	private const float RefreshSeconds = 0.25f;

	private static readonly Vector2 MouseOffset = new Vector2(22f, 26f);

	private static readonly Color Red = new Color(1f, 0.45f, 0.4f, 1f);

	private static readonly Color CellColour = new Color(1f, 0.1f, 0.05f, 0.45f);

	// The most cells marked at once.
	private const int MaxCells = 128;

	private readonly List<XYZ> _cells = new List<XYZ>();

	private readonly List<GameObject> _markers = new List<GameObject>();

	private Material _material;

	internal static Blueprint Hovered;

	private GameObject _canvas;

	private RectTransform _panel;

	private Text _title;

	private Text _body;

	private float _nextRefresh;

	private bool _hasReason;

	private static void Enable()
	{
		Plugin.Root.AddComponent<BuildBlocked>();
		GameSession.OnLeave(() => Hovered = null);
	}

	private void Update()
	{
		if (Hovered != null && (Hovered.IsNullOrReleased() || !Hovered.gameObject.activeInHierarchy))
		{
			Hovered = null;
		}
		if (Hovered == null)
		{
			if (_canvas != null && _canvas.activeSelf)
			{
				_canvas.SetActive(false);
			}
			if (_cells.Count > 0)
			{
				_cells.Clear();
				Mark();
			}
			_nextRefresh = 0f;
			return;
		}
		if (_canvas == null)
		{
			Build();
		}
		if (Time.unscaledTime >= _nextRefresh)
		{
			_nextRefresh = Time.unscaledTime + RefreshSeconds;
			_cells.Clear();
			List<string> reasons = Reasons(Hovered, _cells);
			Mark();
			_hasReason = reasons.Count > 0;
			if (_hasReason)
			{
				_body.text = string.Join("\n", reasons.ToArray());
			}
		}
		if (_canvas.activeSelf != _hasReason)
		{
			_canvas.SetActive(_hasReason);
		}
		if (_hasReason)
		{
			_panel.position = (Vector2)Input.mousePosition + MouseOffset * _canvas.transform.localScale.x;
		}
	}

	private void Build()
	{
		_canvas = CreateCanvas("BuildBlockedCanvas", transform, 941);
		// Only shows: never catches the mouse.
		Destroy(_canvas.GetComponent<GraphicRaycaster>());
		GameObject window = CreateWindow(_canvas.transform, 320f);
		_panel = window.GetComponent<RectTransform>();
		_panel.pivot = Vector2.zero;
		_panel.anchorMin = Vector2.zero;
		_panel.anchorMax = Vector2.zero;
		window.GetComponent<Image>().raycastTarget = false;
		VerticalLayoutGroup layout = window.GetComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(10, 12, 6, 8);
		layout.spacing = 2f;
		_title = CreateText(CreateRow(window.transform, 20f), "Not being built", 14, Red, TextAnchor.MiddleLeft);
		_body = CreateText(window.transform, string.Empty, 13, TextColor, TextAnchor.UpperLeft);
		_body.horizontalOverflow = HorizontalWrapMode.Wrap;
		_canvas.SetActive(false);
	}

	// Red see-through cubes over the cells in the way.
	private void Mark()
	{
		int count = Mathf.Min(_cells.Count, MaxCells);
		for (int i = 0; i < count; i++)
		{
			GameObject marker = Marker(i);
			marker.transform.position = new Vector3(_cells[i].x, _cells[i].y, _cells[i].z);
			if (!marker.activeSelf)
			{
				marker.SetActive(true);
			}
		}
		for (int i = count; i < _markers.Count; i++)
		{
			if (_markers[i].activeSelf)
			{
				_markers[i].SetActive(false);
			}
		}
	}

	private GameObject Marker(int index)
	{
		if (index < _markers.Count)
		{
			return _markers[index];
		}
		if (_material == null)
		{
			_material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
			_material.color = CellColour;
		}
		GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
		marker.name = "BuildBlockedCell";
		Destroy(marker.GetComponent<Collider>());
		marker.transform.SetParent(transform, false);
		marker.transform.localScale = Vector3.one * 1.04f;
		MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = _material;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		_markers.Add(marker);
		return marker;
	}

	// Why the blueprint is not being built, most important first; empty when nothing holds it up. The cells something
	// is in the way in go into cells.
	internal static List<string> Reasons(Blueprint blueprint, List<XYZ> cells)
	{
		List<string> reasons = new List<string>();
		BuildGoal goal = blueprint.GetComponent<BuildGoal>();
		Project project = blueprint.Project;
		if (goal == null)
		{
			return reasons;
		}
		if (!goal.Commited)
		{
			reasons.Add("Its task is still being edited: click away from the task to start it.");
			return reasons;
		}
		if (!blueprint.State.HasFlag(PlacementFlags.valid))
		{
			List<string> what = new List<string>();
			Obstructions(blueprint, cells, what);
			reasons.Add(what.Count > 0
				? "In the way (marked red): " + string.Join(", ", what.ToArray()) + "."
				: "It cannot stand here: nothing holds it up and nothing it rests on is planned.");
		}
		else if (!blueprint.State.HasFlag(PlacementFlags.buildable))
		{
			reasons.Add("Waiting for support: what it rests on or hangs from has to be built first.");
		}
		else if (UnreachableTasks.IsMarked(goal))
		{
			reasons.Add("No bricktron can get to it: a builder has to stand right next to it, so it needs stairs, a ramp, a ladder or a floor to stand on.");
		}
		if (project != null && !project.IsNullOrReleased())
		{
			if (project.PriorityBias == ProjectPriority.Bias.Locked)
			{
				reasons.Add("Its task is paused (locked priority).");
			}
			else if (project.CrewCount() == 0)
			{
				reasons.Add("Its task has no bricktrons: add some to it.");
			}
			if (reasons.Count > 0)
			{
				int stuck = Unreachable(project);
				if (stuck > 0 && !UnreachableTasks.IsMarked(goal))
				{
					reasons.Add(stuck + (stuck == 1 ? " blueprint" : " blueprints") + " of this task no bricktron can get to; what rests on them waits for them.");
				}
			}
		}
		return reasons;
	}

	// The game's obstruction check of a blueprint (BlueprintValidator.IsObstructed), cell by cell: the cells it takes
	// that something stands in, and what.
	private static void Obstructions(Blueprint blueprint, List<XYZ> cells, List<string> what)
	{
		ValidatorComponent validator = blueprint.GetComponent<ValidatorComponent>();
		if (validator == null)
		{
			return;
		}
		ValidationData data = validator.VData;
		IVolume volume = data.Volume;
		if (volume == null)
		{
			return;
		}
		VPropertyMask mask = VPropertyMask.occupy | VPropertyMask.fantome;
		foreach (XYZ cell in volume.GetCases(mask))
		{
			if (!VUtil.IsObstructed(cell, mask, data.Faction, volume))
			{
				continue;
			}
			cells.Add(cell);
			string name = What(cell, mask, data.Faction, volume, blueprint.gameObject);
			if (!what.Contains(name))
			{
				what.Add(name);
			}
		}
	}

	private static string What(XYZ cell, VPropertyMask mask, Faction faction, IVolume volume, GameObject self)
	{
		using (IVoxel voxel = Voxel.GetVoxel(cell))
		{
			if (!voxel.IsNull && !voxel.IsCrust && voxel.IsTerrain)
			{
				return "the ground or a block";
			}
		}
		if (StockpileHeadroom.Blocks(cell, self))
		{
			return "a stockpile's goods";
		}
		if (TreeRegistry.Instance != null && TreeRegistry.Instance.IsNearATree(cell))
		{
			return "a tree";
		}
		foreach (IVolume other in VolumeRegistry.VolumesWithPropertyAtExcluding(cell, mask, volume))
		{
			if (other is BlueprintVolume)
			{
				if (other.Faction == faction)
				{
					return "another blueprint";
				}
				continue;
			}
			GameObject go = other.GameObject;
			if (go == null)
			{
				continue;
			}
			if (go.GetComponentInParent<Labor>() != null)
			{
				return "a bricktron";
			}
			return go.name.Replace("(Clone)", string.Empty).Trim();
		}
		return "something";
	}

	// The task's buildable blueprints no bricktron can get to.
	private static int Unreachable(Project project)
	{
		BuildGoalProvider provider = project.GetComponent<BuildGoalProvider>();
		if (provider == null)
		{
			return 0;
		}
		int count = 0;
		foreach (Blueprint blueprint in provider.TrackedBlueprints)
		{
			if (blueprint.IsNullOrReleased() || !blueprint.State.HasFlag(PlacementFlags.buildable))
			{
				continue;
			}
			if (UnreachableTasks.IsMarked(blueprint.GetComponent<BuildGoal>()))
			{
				count++;
			}
		}
		return count;
	}
}

// The blueprint under the mouse, from the game's hover of blueprints (also the empty ones of the command mode).
[Feature(Features.BuildBlocked, Features.BuildBlockedInfo)]
[HarmonyPatch(typeof(BlueprintsHighlightDriver), "OnObjectHovered")]
internal static class BuildBlockedHoverPatch
{
	private static void Postfix(Blueprint blueprint)
	{
		if (!blueprint.IsNullOrReleased() && !blueprint.IsAnti && !UIGameSelector.IsSelected(blueprint.gameObject))
		{
			BuildBlocked.Hovered = blueprint;
		}
	}
}

[Feature(Features.BuildBlocked, Features.BuildBlockedInfo)]
[HarmonyPatch(typeof(CommandSelectionPicker), nameof(CommandSelectionPicker.OnEnterBlueprint))]
internal static class BuildBlockedEnterPatch
{
	private static void Postfix(Blueprint hoveredBlueprint)
	{
		if (!hoveredBlueprint.IsNullOrReleased() && !hoveredBlueprint.IsAnti && !UIGameSelector.IsSelected(hoveredBlueprint.gameObject))
		{
			BuildBlocked.Hovered = hoveredBlueprint;
		}
	}
}

[Feature(Features.BuildBlocked, Features.BuildBlockedInfo)]
[HarmonyPatch(typeof(CommandSelectionPicker), nameof(CommandSelectionPicker.OnLeaveBlueprint))]
internal static class BuildBlockedLeavePatch
{
	private static void Postfix()
	{
		BuildBlocked.Hovered = null;
	}
}

[Feature(Features.BuildBlocked, Features.BuildBlockedInfo)]
[HarmonyPatch(typeof(BlueprintsHighlightDriver), "OnObjectUnhovered")]
internal static class BuildBlockedUnhoverPatch
{
	private static void Postfix(Blueprint blueprint)
	{
		if (BuildBlocked.Hovered == blueprint)
		{
			BuildBlocked.Hovered = null;
		}
	}
}
