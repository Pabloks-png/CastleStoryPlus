using CastleStoryPlus.Core;
using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game.Semantique;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.KnowledgeSpace;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Pathfinding;
using Brix.Pathfinding.Test;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.Pathfinding;
using CastleStoryPlus.WorkerAI;
using Smooth.Slinq;
using UnityEngine;
using UnityEngine.Networking;
using static CastleStoryPlus.DevTools.Endpoints.Json;
using Object = UnityEngine.Object;
using Tuple2 = Smooth.Algebraics.Tuple<UnityEngine.GameObject, UnityEngine.GameObject>;

namespace CastleStoryPlus.DevTools.Endpoints;

// Why a worker can or cannot get to a thing, by the game's own knowledge (regions of connected access cells, and the
// "A cannot reach B" exclusions left by failed walks) and by the mod's reachability map; and why a workshop has or
// has not work for a worker, with the checks of CraftingStation.HasWorkFor one by one.
internal static class ReachEndpoints
{
	// Most access cells listed per thing.
	private const int MaxCells = 40;

	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/reach", "Whether a bricktron can get to a thing by the game's knowledge (CanReach, the regions of both, the thing's access cells with their route nodes, exclusions from failed walks) and by the mod's reachability map (UnreachableTasks)", Reach)
		{
			Parameters = "id (object id: workshop, blueprint, stockpile, item...; required), unit (bricktron id; default: the player's first builder)"
		});
		routes.Add(new Route("GET", "/api/workshops/why", "Why a workshop has or has not work for a bricktron: every check of the game's HasWorkFor (room, reach, store, fetch, work) with its answer, the priority and whether free workers see it", Why)
		{
			Parameters = "unit (bricktron id; default: the workshop's operator, else the player's first builder), id (workshop id; default: every workshop of the player), cells (true: also each workshop's access cells)"
		});
	}

	private static void NeedHost()
	{
		if (!NetworkServer.active)
		{
			throw new ApiException(409, "Only on the host (single player or hosting a game)");
		}
		if (User.LocalUser == null || User.LocalUser.faction == null || !Architecte.commence || Architecte.termine)
		{
			throw new ApiException(409, "No game in progress (or still loading)");
		}
		if (Knowledge.Instance == null)
		{
			throw new ApiException(409, "The game's knowledge is not up yet");
		}
	}

	private static Labor FindLabor(int unit)
	{
		Labor first = null;
		foreach (Labor labor in Object.FindObjectsOfType<Labor>())
		{
			if (labor == null || labor.faction != User.LocalUser.faction)
			{
				continue;
			}
			if (unit != 0)
			{
				if (labor.gameObject.InstanceId() == unit)
				{
					return labor;
				}
				continue;
			}
			if (first == null && labor.Profession != null && labor.Profession.OccupationType == Occupation.Type.Builder)
			{
				first = labor;
			}
		}
		if (first == null)
		{
			throw new ApiException(404, unit != 0 ? "No bricktron " + unit : "The player has no builder");
		}
		return first;
	}

	private static GameComponent FindComponent(int id)
	{
		foreach (GameComponent component in Object.FindObjectsOfType<GameComponent>())
		{
			if (component != null && component.gameObject.InstanceId() == id)
			{
				return component;
			}
		}
		throw new ApiException(404, "No object " + id + " (GET /api/objects, /api/workshops, /api/blueprints list ids)");
	}

	private static object Reach(ApiRequest request)
	{
		NeedHost();
		GameComponent target = FindComponent(request.Int("id", 0));
		Labor labor = FindLabor(request.Int("unit", 0));
		return Obj(
			"unit", Obj("id", labor.gameObject.InstanceId(), "name", labor.name),
			"target", Obj("id", target.gameObject.InstanceId(), "type", target.name.Replace("(Clone)", string.Empty).Trim(), "position", Vec(target.transform.position)),
			"reach", ReachInfo(labor, target, cells: true));
	}

	// The game's and the mod's view of the way from the worker to the thing.
	private static Dictionary<string, object> ReachInfo(Labor labor, GameComponent target, bool cells)
	{
		Knowledge knowledge = Knowledge.Instance;
		Dictionary<GameObject, List<KnowledgeRegion>> regionsOf = (knowledge.knowledgeUpdater != null) ? knowledge.knowledgeUpdater.RegionsOf : null;
		List<KnowledgeRegion> unitRegions = null;
		List<KnowledgeRegion> targetRegions = null;
		if (regionsOf != null)
		{
			regionsOf.TryGetValue(labor.gameObject, out unitRegions);
			regionsOf.TryGetValue(target.gameObject, out targetRegions);
		}
		int shared = 0;
		int memberMask = 0;
		if (unitRegions != null && target.volume != null)
		{
			foreach (KnowledgeRegion region in unitRegions)
			{
				if (region != null && region.Members.TryGetValue(target.volume, out int mask))
				{
					shared++;
					memberMask |= mask;
				}
			}
		}
		int exclusions = 0;
		if (knowledge.knowledgeExcluder != null)
		{
			knowledge.knowledgeExcluder.Exclusions2.TryGetValue(new Tuple2(labor.gameObject, target.gameObject), out exclusions);
		}
		XYZ unitVoxel = (labor.presence != null) ? labor.presence.CurrentVoxel : XYZ.zero;
		PathNode unitNode = (labor.presence != null) ? labor.presence.CurrentPathNode : null;
		Dictionary<string, object> result = Obj(
			"canReach", knowledge.CanReach(labor, target),
			"knowsAbout", knowledge.KnowsAbout(target.gameObject),
			"unitRegions", (unitRegions != null) ? unitRegions.Count : 0,
			"targetRegions", (targetRegions != null) ? targetRegions.Count : 0,
			"sharedRegions", shared,
			"memberAccessMask", memberMask,
			"exclusionMask", exclusions,
			"excluded", (memberMask & ~exclusions) == 0,
			"regionsKnown", (regionsOf != null) ? regionsOf.Count : 0,
			"unitVoxel", new int[3] { unitVoxel.x, unitVoxel.y, unitVoxel.z },
			"unitNode", (unitNode != null) ? unitNode.GetType().Name + (unitNode.IsEmpty ? " (empty)" : string.Empty) + " lod " + unitNode.Lod : null,
			"modMarked", UnreachableTasks.IsMarked(target),
			"modMarkedWhy", UnreachableTasks.WhyMarked(target),
			"modMapHas", ReachMap.Has(labor.faction));
		if (cells)
		{
			result["accessCells"] = AccessCells(labor.faction, target.volume);
		}
		return result;
	}

	// The thing's access cells, each with the route node there and whether the mod's map has it.
	private static List<object> AccessCells(Faction faction, IVolume volume)
	{
		List<object> result = new List<object>();
		if (volume == null)
		{
			return result;
		}
		bool graph = TestPathfinding.Instance != null && TestPathfinding.Instance.Initialized && TestPathfinding.Instance.terrainLod != null;
		int index = 0;
		foreach (XYZ cell in volume.GetCases(VProperty.access))
		{
			if (result.Count >= MaxCells)
			{
				break;
			}
			PathNode node = graph ? TestPathfinding.Instance.terrainLod.GetPathNode(cell) : null;
			result.Add(Obj(
				"index", index,
				"at", new int[3] { cell.x, cell.y, cell.z },
				"node", (node != null) ? node.GetType().Name + (node.IsEmpty ? " (empty)" : string.Empty) : null,
				"accessCount", (node != null) ? node.AccessCount : 0,
				"full", Voxel.IsFull(cell),
				"onMap", ReachMap.Contains(faction, cell)));
			index++;
		}
		return result;
	}

	// A description's resources as "Type amount".
	private static List<string> Desc(Description description)
	{
		List<string> result = new List<string>();
		if (description == null)
		{
			return result;
		}
		foreach (Ressource resource in description.GetRessources().ToList())
		{
			result.Add(resource.GetType().Name + " " + ((resource.quantifiable != null) ? resource.quantifiable.valeur : 0));
		}
		return result;
	}

	private static object Why(ApiRequest request)
	{
		NeedHost();
		int id = request.Int("id", 0);
		int unit = request.Int("unit", 0);
		bool cells = request.Bool("cells", false);
		List<object> workshops = new List<object>();
		foreach (CraftingStation station in Object.FindObjectsOfType<CraftingStation>())
		{
			if (station == null || station.faction != User.LocalUser.faction || (id != 0 && station.gameObject.InstanceId() != id))
			{
				continue;
			}
			Labor labor = (unit == 0 && station.Operator != null) ? station.Operator : FindLabor(unit);
			workshops.Add(Describe(station, labor, cells));
		}
		if (id != 0 && workshops.Count == 0)
		{
			throw new ApiException(404, "No workshop " + id + " (GET /api/workshops lists them)");
		}
		return Obj("count", workshops.Count, "workshops", workshops);
	}

	// The checks of CraftingStation.HasWorkFor and of the free workers' project list, one by one.
	private static object Describe(CraftingStation station, Labor labor, bool cells)
	{
		List<IOperable<Labor>> available = new List<IOperable<Labor>>();
		ProjectDatabase.For(labor.faction).AvailableProjects(available, labor);
		Recipe recipe = station.CurrentRecipe;
		Description stuff = station.StuffToFetch();
		string decide;
		try
		{
			decide = station.DecideWhatToDo(labor).ToString();
		}
		catch (Exception e)
		{
			decide = "threw " + e.GetType().Name + ": " + e.Message;
		}
		GameObject product = null;
		bool itemToPickup = station.inventory != null && station.inventory.ItemToPickup(out product);
		List<string> storable = new List<string>();
		if (station.inventory != null)
		{
			foreach (Factory.AssetKey key in station.inventory.uniqueStorable)
			{
				storable.Add(key.ToString());
			}
		}
		Apparence apparence = (product != null) ? product.GetComponent<Apparence>() : null;
		Ressource principal = (apparence != null) ? apparence.Principale : null;
		GameObject bestStore = (product != null) ? labor.BestRecepteurToStore(product) : null;
		return Obj(
			"id", station.gameObject.InstanceId(),
			"type", station.name.Replace("(Clone)", string.Empty).Trim(),
			"stationContent", Desc(station.recepteur.ContentDescription),
			"stationBaseCapacity", Desc(station.recepteur.BaseCapacity),
			"unitBaseCapacity", Desc(labor.recepteur.BaseCapacity),
			"unitCurrentCapacity", Desc(labor.recepteur.CurrentCapacity),
			"toStore", (station.inventory != null) ? Desc(station.inventory.toStore) : null,
			"uniqueStorable", storable,
			"itemToPickup", itemToPickup,
			"product", (product != null) ? product.name : null,
			"productResource", (principal != null) ? principal.GetType().Name : null,
			"productDescription", (apparence != null) ? Desc(apparence.description) : null,
			"productRepresentative", (principal != null) ? Description.RepresentativeOf(principal)?.ToString() : null,
			"unitRoomForProduct", product != null && labor.recepteur.HasRoomFor(product),
			"unitBaseCapacityForProduct", (principal != null) ? labor.recepteur.BaseCapacity.Value(principal) : -1,
			"unitCurrentCapacityForProduct", (principal != null) ? labor.recepteur.CurrentCapacity.Value(principal) : -1,
			"bestStoreForProduct", (bestStore != null) ? bestStore.name + " " + bestStore.InstanceId() : null,
			"recipeCreates", recipe?.Creates?.ToString(),
			"recipeNeedsWork", recipe != null && recipe.NeedsWork,
			"recipeDropOnCompletion", recipe != null && recipe.DropOnCompletion,
			"hasIngredients", recipe != null && recipe.HasIngredientsIn(station.recepteur),
			"stationRoomForCreates", recipe != null && recipe.Creates != null && station.recepteur.HasRoomFor(recipe.Creates),
			"unit", Obj("id", labor.gameObject.InstanceId(), "name", labor.name, "autonomy", labor.Autonomy?.Id.ToString(), "project", (labor.Project != null) ? labor.Project.GetType().Name : null, "operable", (labor.Operable != null) ? labor.Operable.ToString() : null),
			"operator", (station.Operator != null) ? station.Operator.name : null,
			"priorityBias", station.PriorityBias.ToString(),
			"listedForFreeWorkers", available.Contains(station),
			"hasWorkFor", station.HasWorkFor(labor),
			"hasRoomFor", station.HasRoomFor(labor),
			"decide", decide,
			"canReach", Knowledge.Instance.CanReach(labor, station),
			"canStore", station.CanStore(labor),
			"autoStore", station.AutoStore,
			"canPickUpProduction", station.CanPickUpProduction(labor, andStore: false),
			"canPickUpAndStore", station.CanPickUpProduction(labor, andStore: true),
			"canFetch", station.CanFetch(labor),
			"stuffToFetch", stuff?.ToString(),
			"currentRecipe", recipe?.ToString(),
			"needsWork", station.NeedsWork(recipe),
			"canWork", station.CanWork(labor),
			"unitHandsFree", labor.recepteur != null && labor.recepteur.IsEmpty(),
			"reach", ReachInfo(labor, station, cells));
	}
}
