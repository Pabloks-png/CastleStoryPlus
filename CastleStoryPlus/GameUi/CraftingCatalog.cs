using System;
using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Semantique;
using Brix.Lua;
using Brix.UI.Icons;
using Smooth.Slinq;
using UnityEngine;

namespace CastleStoryPlus.GameUi;

// The workshops' recipes and the resources they use (LUI/Meta/Meta_Operable.lua, Meta_Recipe.lua and
// Meta_Resource.lua) as C# data, keyed by the game's Lua enums (LuaCrafting.LuaRecipe and LuaResource) as the
// queue and the crafting commands use them. The mod's own recipes and resources are added by their features.
internal static class CraftingCatalog
{
	internal sealed class Named
	{
		public Func<string> Name;

		public Func<Sprite> Icon;

		// A resource's icon as the stockpiles show it (Meta_Resource's StockpiledIcon).
		public Func<Sprite> Stockpiled;
	}

	// Per workshop type (its LuaDriver handle: fy_Furnace...), the recipes in the order its menu shows them.
	internal static readonly Dictionary<string, List<LuaCrafting.LuaRecipe>> Workshops = new Dictionary<string, List<LuaCrafting.LuaRecipe>>
	{
		{ "fy_Furnace", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.IronIngot, LuaCrafting.LuaRecipe.GlassIngot } },
		{ "fy_PrimitiveWorkbench", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.Planks } },
		{ "fy_Loom", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.FabricPile, LuaCrafting.LuaRecipe.RopeBallot } },
		{ "fy_Workbench", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.HalberdierKit, LuaCrafting.LuaRecipe.ArcherKit, LuaCrafting.LuaRecipe.LanternWard } },
		{ "fy_Anvil", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.KnightKit, LuaCrafting.LuaRecipe.Cog } },
		{ "fy_MachineShop", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.ArbalistKit, LuaCrafting.LuaRecipe.BearTrap } },
		{ "fy_Laboratory", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.AlchemistKit, LuaCrafting.LuaRecipe.Bomb, LuaCrafting.LuaRecipe.ExplosiveBarrel } },
		{ "fy_Altar", new List<LuaCrafting.LuaRecipe> { LuaCrafting.LuaRecipe.ArtificerKit, LuaCrafting.LuaRecipe.SentinelWard, LuaCrafting.LuaRecipe.HealingWard, LuaCrafting.LuaRecipe.CaptureWard, LuaCrafting.LuaRecipe.PylonWard } }
	};

	private static readonly Dictionary<LuaCrafting.LuaRecipe, Named> Recipes = new Dictionary<LuaCrafting.LuaRecipe, Named>();

	private static readonly Dictionary<LuaCrafting.LuaResource, Named> Resources = new Dictionary<LuaCrafting.LuaResource, Named>();

	// The resources in Meta_Resource's order, the mod's after the game's (the resource list's rows).
	internal static readonly List<LuaCrafting.LuaResource> ResourceOrder = new List<LuaCrafting.LuaResource>();

	static CraftingCatalog()
	{
		Recipe(LuaCrafting.LuaRecipe.None, "none", IconKeys.Cancel);
		Recipe(LuaCrafting.LuaRecipe.Planks, "planks", IconKeys._Plank);
		Recipe(LuaCrafting.LuaRecipe.IronIngot, "ironingot", IconKeys._Iron);
		Recipe(LuaCrafting.LuaRecipe.GlassIngot, "glassingot", IconKeys._Glass_Ingot);
		Recipe(LuaCrafting.LuaRecipe.FabricPile, "fabricpile", IconKeys._Fabric);
		Recipe(LuaCrafting.LuaRecipe.RopeBallot, "ropeballot", IconKeys._Rope);
		Recipe(LuaCrafting.LuaRecipe.Cog, "cog", IconKeys._Cog);
		Recipe(LuaCrafting.LuaRecipe.Bomb, "bomb", IconKeys._Bomb);
		Recipe(LuaCrafting.LuaRecipe.BearTrap, "beartrap", IconKeys._Beartrap);
		Recipe(LuaCrafting.LuaRecipe.ExplosiveBarrel, "explosivebarrel", IconKeys._Explosive_Barrel);
		Recipe(LuaCrafting.LuaRecipe.SentinelWard, "sentinelward", IconKeys._Sentinel);
		Recipe(LuaCrafting.LuaRecipe.HealingWard, "healingward", IconKeys._Healing_Ward);
		Recipe(LuaCrafting.LuaRecipe.CaptureWard, "captureward", IconKeys._Capture_Stone);
		Recipe(LuaCrafting.LuaRecipe.LanternWard, "lanternward", IconKeys._Lantern);
		Recipe(LuaCrafting.LuaRecipe.PylonWard, "pylonward", IconKeys._Pylon);
		Recipe(LuaCrafting.LuaRecipe.Poleaxe, "poleaxe", IconKeys._Poleaxe);
		Recipe(LuaCrafting.LuaRecipe.KettleHat, "kettlehat", IconKeys._Kettle_Hat);
		Recipe(LuaCrafting.LuaRecipe.Sword, "sword", IconKeys._Sword);
		Recipe(LuaCrafting.LuaRecipe.Shield, "shield", IconKeys._Shield);
		Recipe(LuaCrafting.LuaRecipe.Helmet, "helmet", IconKeys._Helmet);
		Recipe(LuaCrafting.LuaRecipe.Bow, "bow", IconKeys._Bow);
		Recipe(LuaCrafting.LuaRecipe.Quiver, "quiver", IconKeys._Quiver);
		Recipe(LuaCrafting.LuaRecipe.Cap, "cap", IconKeys._Cap);
		Recipe(LuaCrafting.LuaRecipe.CrossbowWeapon, "arbalistweapon", IconKeys._Crossbow);
		Recipe(LuaCrafting.LuaRecipe.CrossbowPouch, "arbalistpouch", IconKeys._Pouch);
		Recipe(LuaCrafting.LuaRecipe.CrossbowMorion, "arbalistcap", IconKeys._Kepi);
		Recipe(LuaCrafting.LuaRecipe.Staff, "artificerstaff", IconKeys._Staff);
		Recipe(LuaCrafting.LuaRecipe.Sachel, "artificersatchel", IconKeys._Satchel);
		Recipe(LuaCrafting.LuaRecipe.ArtificerHat, "artificerhat", IconKeys._Artificer_Hat);
		Recipe(LuaCrafting.LuaRecipe.AlchemistPouch, "alchemistpouch", IconKeys._Backpack);
		Recipe(LuaCrafting.LuaRecipe.AlchemistHat, "alchemisthat", IconKeys._Bonnet);
		Recipe(LuaCrafting.LuaRecipe.HalberdierKit, "halberdierkit", IconKeys._Halberdier_Kit);
		Recipe(LuaCrafting.LuaRecipe.KnightKit, "knightkit", IconKeys._Knight_Kit);
		Recipe(LuaCrafting.LuaRecipe.ArcherKit, "archerkit", IconKeys._Archer_Kit);
		Recipe(LuaCrafting.LuaRecipe.ArbalistKit, "arbalistkit", IconKeys._Arbalist_Kit);
		Recipe(LuaCrafting.LuaRecipe.ArtificerKit, "artificerkit", IconKeys._Artificer_Kit);
		Recipe(LuaCrafting.LuaRecipe.AlchemistKit, "alchemistkit", IconKeys._Alchemist_Kit);

		Resource(LuaCrafting.LuaResource.None, "empty", IconKeys._Stockpile_Empty, IconKeys._Stockpile_Empty);
		Resource(LuaCrafting.LuaResource.WoodBlock, "woodblock", IconKeys._Log, IconKeys._Stockpiled_Log);
		Resource(LuaCrafting.LuaResource.StoneBlock, "stoneblock", IconKeys._Brick, IconKeys._Stockpiled_Brick);
		Resource(LuaCrafting.LuaResource.PlankBlock, "plankblock", IconKeys._Plank, IconKeys._Stockpiled_Plank);
		Resource(LuaCrafting.LuaResource.OrangeCrystal, "orangecrystal", IconKeys._Raw_Orange_Crystal, IconKeys._Stockpiled_Raw_Orange_Crystal);
		Resource(LuaCrafting.LuaResource.Stones, "stones", IconKeys._Raw_Stone, IconKeys._Stockpiled_Raw_Stone);
		Resource(LuaCrafting.LuaResource.Iron, "iron", IconKeys._Iron, IconKeys._Stockpiled_Iron);
		Resource(LuaCrafting.LuaResource.BlueCrystal, "bluecrystal", IconKeys._Raw_Blue_Crystal, IconKeys._Stockpiled_Raw_Blue_Crystal);
		Resource(LuaCrafting.LuaResource.Plant, "plant", IconKeys._Plant, IconKeys._Plant);
		Resource(LuaCrafting.LuaResource.Rope, "rope", IconKeys._Rope, IconKeys._Rope);
		Resource(LuaCrafting.LuaResource.Fabric, "fabric", IconKeys._Fabric, IconKeys._Fabric);
		Resource(LuaCrafting.LuaResource.RawIron, "rawiron", IconKeys._Raw_Iron, IconKeys._Stockpiled_Raw_Iron);
		Resource(LuaCrafting.LuaResource.Glass, "glass", IconKeys._Glass_Ingot, IconKeys._Stockpiled_Glass);
		Resource(LuaCrafting.LuaResource.Cog, "cog", IconKeys._Cog, IconKeys._Stockpiled_Cog);
		Resource(LuaCrafting.LuaResource.Bomb, "bomb", IconKeys._Bomb, IconKeys._Bomb);
		Resource(LuaCrafting.LuaResource.ExplosiveBarrel, "explosivebarrel", IconKeys._Explosive_Barrel, IconKeys._Explosive_Barrel);
		Resource(LuaCrafting.LuaResource.SentinelWard, "sentinelward", IconKeys._Sentinel, IconKeys._Sentinel);
		Resource(LuaCrafting.LuaResource.HealingWard, "healingward", IconKeys._Healing_Ward, IconKeys._Healing_Ward);
		Resource(LuaCrafting.LuaResource.CaptureWard, "captureward", IconKeys._Shield_Ward, IconKeys._Shield_Ward);
		Resource(LuaCrafting.LuaResource.LanternWard, "lanternward", IconKeys._Lantern, IconKeys._Lantern);
		Resource(LuaCrafting.LuaResource.PylonWard, "pylonward", IconKeys._Pylon, IconKeys._Pylon);
	}

	private static void Recipe(LuaCrafting.LuaRecipe recipe, string key, IconKey icon)
	{
		string name = "##recipe_" + key;
		Recipes[recipe] = new Named { Name = () => GameUiState.Text(name), Icon = () => GameUiState.Icon(icon) };
	}

	private static void Resource(LuaCrafting.LuaResource resource, string key, IconKey icon, IconKey stockpiled)
	{
		string name = "##resource_" + key;
		Resources[resource] = new Named { Name = () => GameUiState.Text(name), Icon = () => GameUiState.Icon(icon), Stockpiled = () => GameUiState.Icon(stockpiled) };
		ResourceOrder.Add(resource);
	}

	// A feature's own recipe (its name and icon).
	internal static void AddRecipe(LuaCrafting.LuaRecipe recipe, string name, Func<Sprite> icon)
	{
		Recipes[recipe] = new Named { Name = () => name, Icon = icon };
	}

	internal static void AddResource(LuaCrafting.LuaResource resource, string name, Func<Sprite> icon, Func<Sprite> stockpiled = null)
	{
		Resources[resource] = new Named { Name = () => name, Icon = icon, Stockpiled = stockpiled ?? icon };
		if (!ResourceOrder.Contains(resource))
		{
			ResourceOrder.Add(resource);
		}
	}

	// A recipe added to a workshop's menu, after another of its recipes (or at the end).
	internal static void AddToWorkshop(string workshop, LuaCrafting.LuaRecipe recipe, LuaCrafting.LuaRecipe? after = null)
	{
		if (!Workshops.TryGetValue(workshop, out List<LuaCrafting.LuaRecipe> recipes))
		{
			recipes = new List<LuaCrafting.LuaRecipe>();
			Workshops[workshop] = recipes;
		}
		if (recipes.Contains(recipe))
		{
			return;
		}
		int at = (after != null) ? recipes.IndexOf(after.Value) : -1;
		recipes.Insert((at < 0) ? recipes.Count : at + 1, recipe);
	}

	internal static string RecipeName(LuaCrafting.LuaRecipe recipe)
	{
		return Recipes.TryGetValue(recipe, out Named named) ? named.Name() : recipe.ToString();
	}

	internal static Sprite RecipeIcon(LuaCrafting.LuaRecipe recipe)
	{
		return Recipes.TryGetValue(recipe, out Named named) && named.Icon != null ? named.Icon() : null;
	}

	internal static string ResourceName(LuaCrafting.LuaResource resource)
	{
		return Resources.TryGetValue(resource, out Named named) ? named.Name() : resource.ToString();
	}

	internal static Sprite ResourceIcon(LuaCrafting.LuaResource resource)
	{
		return Resources.TryGetValue(resource, out Named named) && named.Icon != null ? named.Icon() : null;
	}

	internal static Sprite StockpiledIcon(LuaCrafting.LuaResource resource)
	{
		return Resources.TryGetValue(resource, out Named named) && named.Stockpiled != null ? named.Stockpiled() : null;
	}

	// What the local player's stockpiles hold, per resource (FactionStorage.GetContent; None: the empty stockpiles).
	private static readonly HashSet<string> Unmapped = new HashSet<string>();

	internal static Dictionary<LuaCrafting.LuaResource, int> Stored()
	{
		EnsureMapped();
		Dictionary<LuaCrafting.LuaResource, int> stored = new Dictionary<LuaCrafting.LuaResource, int>();
		Brix.Game.Faction faction = (Brix.Game.User.LocalUser != null) ? Brix.Game.User.LocalUser.faction : null;
		if (faction == null || faction.Storage == null || faction.Storage.storage == null)
		{
			return stored;
		}
		foreach (Ressource ressource in faction.Storage.storage.ContentDescription.GetRessources().ToList())
		{
			if (ressource.valeur == 0)
			{
				continue;
			}
			// A resource without a Lua value (no menu shows it) is left out; ToLuaResource would throw for it.
			if (!LuaCrafting._adjectiveToLuaResource.ContainsKey(ressource.GetKey()))
			{
				if (Unmapped.Add(ressource.GetType().Name))
				{
					Plugin.Log.LogWarning("CraftingCatalog: " + ressource.GetType().Name + " has no Lua resource; left out of the resource list");
				}
				continue;
			}
			LuaCrafting.LuaResource resource = LuaCrafting.ToLuaResource(ressource);
			stored[resource] = ressource.valeur;
		}
		return stored;
	}

	internal static List<LuaCrafting.LuaRecipe> RecipesOf(CraftingStation station)
	{
		string type = (station != null) ? GameUiState.TypeOf(station) : null;
		return (type != null && Workshops.TryGetValue(type, out List<LuaCrafting.LuaRecipe> recipes)) ? recipes : null;
	}

	// The game's lookup tables between its Lua enums and recipes, made when a Lua script registers them; made here
	// when no script did yet.
	internal static void EnsureMapped()
	{
		if (!LuaCrafting._isRegistered)
		{
			LuaCrafting.MapRecipes(out LuaCrafting._luaRecipeToRecipeInfo, out LuaCrafting._recipeInfoToLuaRecipe);
			LuaCrafting.MapResources(out LuaCrafting._luaResourceToResource, out LuaCrafting._adjectiveToLuaResource);
			LuaCrafting._isRegistered = true;
		}
	}

	// The resources a recipe takes and how many of each (Data_Recipe's input).
	internal static List<KeyValuePair<LuaCrafting.LuaResource, int>> Inputs(LuaCrafting.LuaRecipe recipe)
	{
		EnsureMapped();
		Dictionary<LuaCrafting.LuaResource, int> counts = new Dictionary<LuaCrafting.LuaResource, int>();
		List<LuaCrafting.LuaResource> order = new List<LuaCrafting.LuaResource>();
		List<RecipeInfo> infos = LuaCrafting.ToRecipeInfo(recipe);
		if (infos == null)
		{
			return new List<KeyValuePair<LuaCrafting.LuaResource, int>>();
		}
		foreach (RecipeInfo info in infos)
		{
			foreach (Smooth.Algebraics.Tuple<AdjectiveKey, int> ingredient in info.Ingredients())
			{
				LuaCrafting.LuaResource resource = LuaCrafting.ToLuaResource(ingredient._1);
				if (!counts.ContainsKey(resource))
				{
					counts[resource] = 0;
					order.Add(resource);
				}
				counts[resource] += ingredient._2;
			}
			// The recipe's heat, as orange crystal (LuaExtenders.ToDynValue).
			if (info.heat > 0)
			{
				if (!counts.ContainsKey(LuaCrafting.LuaResource.OrangeCrystal))
				{
					counts[LuaCrafting.LuaResource.OrangeCrystal] = 0;
					order.Add(LuaCrafting.LuaResource.OrangeCrystal);
				}
				counts[LuaCrafting.LuaResource.OrangeCrystal] += info.heat;
			}
		}
		List<KeyValuePair<LuaCrafting.LuaResource, int>> result = new List<KeyValuePair<LuaCrafting.LuaResource, int>>();
		foreach (LuaCrafting.LuaResource resource in order)
		{
			result.Add(new KeyValuePair<LuaCrafting.LuaResource, int>(resource, counts[resource]));
		}
		return result;
	}

	// What the workshop holds, per resource (CraftingStation.GetContent).
	internal static Dictionary<LuaCrafting.LuaResource, int> Content(CraftingStation station)
	{
		EnsureMapped();
		Dictionary<LuaCrafting.LuaResource, int> content = new Dictionary<LuaCrafting.LuaResource, int>();
		if (station == null || station.recepteur == null)
		{
			return content;
		}
		foreach (Ressource ressource in station.recepteur.ContentDescription.GetRessources().ToList())
		{
			if (ressource.valeur == 0)
			{
				continue;
			}
			LuaCrafting.LuaResource resource = LuaCrafting.ToLuaResource(ressource);
			content.TryGetValue(resource, out int n);
			content[resource] = n + ressource.valeur;
		}
		return content;
	}

	// The queue as recipes (CraftingStation.GetQueue).
	internal static List<LuaCrafting.LuaRecipe> Queue(CraftingStation station)
	{
		EnsureMapped();
		List<LuaCrafting.LuaRecipe> queue = new List<LuaCrafting.LuaRecipe>();
		if (station == null || station.craftingLabor == null)
		{
			return queue;
		}
		foreach (RecipeInfo info in station.QueuedRecipes)
		{
			queue.Add(LuaCrafting.ToLuaRecipe(info));
		}
		return queue;
	}
}
