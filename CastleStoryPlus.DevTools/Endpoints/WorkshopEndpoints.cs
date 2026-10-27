using CastleStoryPlus.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Network;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using UnityEngine.Networking;
using static CastleStoryPlus.DevTools.Endpoints.Json;
using Object = UnityEngine.Object;

namespace CastleStoryPlus.DevTools.Endpoints;

// Workshops (chopping block, workbench, forge, furnace, loom, ...): list them with their worker and queue, give
// them a worker and queue recipes, through the game's own crafting commands (UNetProjectCmd), run on the host.
internal static class WorkshopEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/workshops", "The player's workshops: id, type, position, worker, queued recipes, loop; and every recipe by name", Workshops)
		{
			Parameters = "q (part of the type name)"
		});
		routes.Add(new Route("POST", "/api/workshops/worker", "Gives a workshop its worker, as dragging a bricktron onto it does. Without unit, takes an idle bricktron (else one from a project)", Worker)
		{
			Parameters = "id (workshop id, required), unit (bricktron id)"
		});
		routes.Add(new Route("POST", "/api/workshops/queue", "Queues a recipe at a workshop, as its menu does (e.g. Planks at the chopping block)", Queue)
		{
			Parameters = "id (workshop id, required), recipe (recipe name from GET /api/workshops, required), count (default 1, at most 20), loop (true or false: repeat the queue)"
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
	}

	// The bricktrons working at a workshop: they are on no project, but not idle either.
	internal static HashSet<Labor> Operators()
	{
		HashSet<Labor> result = new HashSet<Labor>();
		foreach (CraftingStation station in Object.FindObjectsOfType<CraftingStation>())
		{
			if (station != null && station.Operator != null)
			{
				result.Add(station.Operator);
			}
		}
		return result;
	}

	private static Dictionary<string, RecipeInfo> Recipes()
	{
		Dictionary<string, RecipeInfo> result = new Dictionary<string, RecipeInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (FieldInfo field in typeof(CookBook).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.FieldType == typeof(RecipeInfo) && field.GetValue(null) is RecipeInfo recipe)
			{
				result[field.Name] = recipe;
			}
		}
		// The workshop menus' recipes made by one order (KnightKit with KitBundles: one kit bundle), by their Lua
		// names.
		if (Brix.Lua.LuaCrafting._luaRecipeToRecipeInfo != null)
		{
			foreach (KeyValuePair<Brix.Lua.LuaCrafting.LuaRecipe, List<RecipeInfo>> pair in Brix.Lua.LuaCrafting._luaRecipeToRecipeInfo)
			{
				string name = pair.Key.ToString();
				if (pair.Value != null && pair.Value.Count == 1 && !result.ContainsKey(name) && !char.IsDigit(name[0]))
				{
					result[name] = pair.Value[0];
				}
			}
		}
		return result;
	}

	private static string RecipeName(RecipeInfo recipe, Dictionary<string, RecipeInfo> recipes)
	{
		foreach (KeyValuePair<string, RecipeInfo> pair in recipes)
		{
			if (pair.Value == recipe)
			{
				return pair.Key;
			}
		}
		return recipe.Creates?.Name ?? recipe.id.ToString();
	}

	private static object Workshops(ApiRequest request)
	{
		NeedHost();
		string filter = request.String("q", string.Empty);
		Dictionary<string, RecipeInfo> recipes = Recipes();
		List<object> workshops = new List<object>();
		foreach (CraftingStation station in Object.FindObjectsOfType<CraftingStation>())
		{
			if (station == null || station.faction != User.LocalUser.faction)
			{
				continue;
			}
			string type = station.name.Replace("(Clone)", string.Empty).Trim();
			if (filter.Length > 0 && type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			List<string> queued = new List<string>();
			foreach (RecipeInfo recipe in station.QueuedRecipes)
			{
				queued.Add(RecipeName(recipe, recipes));
			}
			Labor worker = station.Operator;
			workshops.Add(Obj(
				"id", station.gameObject.InstanceId(),
				"type", type,
				"position", Vec(station.transform.position),
				"worker", (worker != null) ? Obj("id", worker.gameObject.InstanceId(), "name", worker.name) : null,
				"queue", queued,
				"loop", station.craftingLabor.LoopRecipe));
		}
		List<object> known = new List<object>();
		foreach (KeyValuePair<string, RecipeInfo> pair in recipes)
		{
			List<object> ingredients = new List<object>();
			foreach (var ingredient in pair.Value.Ingredients())
			{
				ingredients.Add(Obj("item", ingredient._1.codeName, "amount", ingredient._2));
			}
			known.Add(Obj("name", pair.Key, "creates", pair.Value.Creates?.Name, "amount", pair.Value.QtyCreated, "ingredients", ingredients));
		}
		return Obj("count", workshops.Count, "workshops", workshops, "recipes", known);
	}

	private static CraftingStation Find(int id)
	{
		foreach (CraftingStation station in Object.FindObjectsOfType<CraftingStation>())
		{
			if (station != null && station.gameObject.InstanceId() == id)
			{
				if (station.faction != User.LocalUser.faction)
				{
					throw new ApiException(409, "Workshop " + id + " is not the player's");
				}
				return station;
			}
		}
		throw new ApiException(404, "No workshop " + id + " (GET /api/workshops lists them)");
	}

	private static object Worker(ApiRequest request)
	{
		NeedHost();
		CraftingStation station = Find(request.Int("id", 0));
		int unit = request.Int("unit", 0);
		Labor labor = null;
		Labor busy = null;
		HashSet<Labor> operators = Operators();
		foreach (Labor candidate in Object.FindObjectsOfType<Labor>())
		{
			if (candidate == null || candidate.faction != User.LocalUser.faction || (unit == 0 && operators.Contains(candidate)))
			{
				continue;
			}
			if (unit != 0)
			{
				if (candidate.gameObject.InstanceId() == unit)
				{
					labor = candidate;
					break;
				}
				continue;
			}
			if (candidate.Profession == null || candidate.Profession.OccupationType != Occupation.Type.Builder)
			{
				continue;
			}
			if (candidate.Project == null || candidate.Project is IdleProject)
			{
				labor = candidate;
				break;
			}
			if (busy == null)
			{
				busy = candidate;
			}
		}
		labor = labor ?? busy;
		if (labor == null)
		{
			throw new ApiException(404, unit != 0 ? "No bricktron " + unit : "No builder to give the workshop");
		}
		User.LocalUser.GetComponent<UNetProjectCmd>().CmdCraftingAssignWorker(station.gameObject, labor.gameObject);
		return Obj("workshop", station.gameObject.InstanceId(), "worker", Obj("id", labor.gameObject.InstanceId(), "name", labor.name), "operator", station.Operator == labor);
	}

	private static object Queue(ApiRequest request)
	{
		NeedHost();
		CraftingStation station = Find(request.Int("id", 0));
		string name = request.Required("recipe");
		if (!Recipes().TryGetValue(name, out RecipeInfo recipe))
		{
			throw new ApiException(400, "No recipe " + name + " (GET /api/workshops lists them)");
		}
		int count = request.Int("count", 1);
		if (count < 1 || count > 20)
		{
			throw new ApiException(400, "'count' must be from 1 to 20");
		}
		UNetProjectCmd command = User.LocalUser.GetComponent<UNetProjectCmd>();
		for (int i = 0; i < count; i++)
		{
			command.CmdcraftingQueueRecipe(station.gameObject, recipe.id, 1);
		}
		if (request.Get("loop") != null)
		{
			command.CmdcraftingLoop(station.gameObject, request.Bool("loop", false));
		}
		List<string> queued = new List<string>();
		Dictionary<string, RecipeInfo> recipes = Recipes();
		foreach (RecipeInfo item in station.QueuedRecipes)
		{
			queued.Add(RecipeName(item, recipes));
		}
		return Obj("workshop", station.gameObject.InstanceId(), "queue", queued, "loop", station.craftingLabor.LoopRecipe);
	}
}
