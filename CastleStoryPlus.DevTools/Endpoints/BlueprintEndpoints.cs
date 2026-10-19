using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;
using Object = UnityEngine.Object;

namespace CastleStoryPlus.DevTools.Endpoints;

// Why a blueprint is (not) being built: its placement state (placed, valid, buildable: only buildable blueprints are
// tasks of their build project), whether it is a demolition, its material (delivered and still needed), and a fresh
// check of its support: the result, whether something is in its way, and each support cell with what holds it.
internal static class BlueprintEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/blueprints", "Blueprints and why each is or is not built: state (placed, valid, buildable), demolition, material delivered and needed, a fresh support check with each support cell", Blueprints)
		{
			Parameters = "q (part of the type name), max (default 50), cells (false: leave out the support cells)"
		});
	}

	private static object Blueprints(ApiRequest request)
	{
		string filter = request.String("q", string.Empty);
		int max = Mathf.Clamp(request.Int("max", 50), 1, 500);
		bool withCells = request.Bool("cells", true);
		Dictionary<string, int> counts = new Dictionary<string, int>();
		List<object> result = new List<object>();
		foreach (Blueprint blueprint in Object.FindObjectsOfType<Blueprint>())
		{
			if (blueprint == null || blueprint.Released)
			{
				continue;
			}
			FactoryImprint imprint = blueprint.GetComponent<FactoryImprint>();
			string type = (imprint != null && imprint.AssetKey != null) ? (imprint.AssetKey.Factory + "." + imprint.AssetKey.Name) : blueprint.name;
			if (filter.Length > 0 && type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			string summary = Summary(blueprint);
			counts.TryGetValue(summary, out int count);
			counts[summary] = count + 1;
			if (result.Count < max)
			{
				result.Add(Describe(blueprint, type, withCells));
			}
		}
		return Obj("counts", counts, "blueprints", result);
	}

	// What the counts group by: demolition, or how far the blueprint is from being built.
	private static string Summary(Blueprint blueprint)
	{
		if (blueprint.IsAnti)
		{
			return "demolish";
		}
		PlacementFlags state = blueprint.State;
		if (!state.HasFlag(PlacementFlags.placed))
		{
			return "not placed";
		}
		if (!state.HasFlag(PlacementFlags.valid))
		{
			return "not valid";
		}
		if (!state.HasFlag(PlacementFlags.buildable))
		{
			return "waiting for support";
		}
		return (blueprint.recepteur != null && blueprint.recepteur.IsFull()) ? "buildable, full" : "buildable";
	}

	private static object Describe(Blueprint blueprint, string type, bool withCells)
	{
		BuildGoal goal = blueprint.GetComponent<BuildGoal>();
		ValidatorComponent validator = blueprint.GetComponent<ValidatorComponent>();
		string check = null;
		bool? obstructed = null;
		List<object> cells = null;
		if (validator != null && validator.Validator != null)
		{
			try
			{
				ValidationData vData = validator.VData;
				check = validator.Validator.Validate(ref vData).ToString();
				obstructed = validator.IsObstructed();
				if (withCells && vData.Volume != null)
				{
					cells = new List<object>();
					foreach (XYZ cell in vData.Volume.GetCases(VProperty.mainSupportReq))
					{
						cells.Add(Obj("at", Cell(cell), "support", VUtil.SupportStateAt(cell, validator.Validator.SupportSettings, vData.Faction).ToString()));
					}
				}
			}
			catch (Exception ex)
			{
				check = "error: " + ex.Message;
			}
		}
		Project project = blueprint.Project;
		return Obj(
			"id", blueprint.gameObject.GetInstanceID(),
			"type", type,
			"position", Vec(blueprint.transform.position),
			"state", blueprint.State.ToString(),
			"demolish", blueprint.IsAnti,
			"committed", blueprint.commited,
			"project", (project != null) ? project.GetType().Name + " " + project.gameObject.GetInstanceID() : null,
			"workers", (goal != null) ? goal.workers.Count : 0,
			"unreachable", CastleStoryPlus.WorkerAI.UnreachableTasks.IsMarked(goal),
			"hanging", CastleStoryPlus.WorkerAI.UnreachableTasks.IsHanging(goal),
			"markedWhy", CastleStoryPlus.WorkerAI.UnreachableTasks.WhyMarked(goal),
			"mapReaches", CastleStoryPlus.WorkerAI.UnreachableTasks.MapReaches(blueprint.faction, goal),
			"workCells", WorkCells(goal),
			"delivered", (blueprint.recepteur != null && !blueprint.recepteur.IsEmpty()) ? blueprint.recepteur.ContentString() : null,
			"full", blueprint.recepteur != null && blueprint.recepteur.IsFull(),
			"check", check,
			"obstructed", obstructed,
			"maxMissingSupport", (validator != null) ? validator.MaxMissingSupport : 0,
			"supportCells", cells);
	}

	// Where a worker stands to build it (access and operation cells).
	private static List<int[]> WorkCells(BuildGoal goal)
	{
		if (goal == null || goal.volume == null)
		{
			return null;
		}
		List<int[]> cells = new List<int[]>();
		foreach (VProperty property in new[] { VProperty.access, VProperty.operation })
		{
			foreach (XYZ cell in goal.volume.GetCases(property))
			{
				cells.Add(Cell(cell));
			}
		}
		return cells;
	}

	private static int[] Cell(XYZ cell)
	{
		return new int[3] { cell.x, cell.y, cell.z };
	}
}
