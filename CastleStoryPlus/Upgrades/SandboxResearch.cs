using System.Collections.Generic;
using System.Reflection;
using Brix.Engine;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Upgrades;

// In sandbox every research is free: there are no enemies, so no dark crystals, and the upgrades would be out of
// reach. A research still has to be queued and worked at its station for its usual time, it just needs no materials.
// The research recipes are shared by all games, so each time a world is ready their costs are emptied for a sandbox
// game and put back for any other. Every machine does this itself (the gamemode is the same everywhere); nothing is
// saved.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
internal static class SandboxResearch
{
	private static readonly FieldInfo IngredientsField = AccessTools.Field(typeof(RecipeInfo), "ingredients");

	// The real costs, kept while they are emptied.
	private static readonly Dictionary<RecipeInfo, Description> Ingredients = new Dictionary<RecipeInfo, Description>();

	private static readonly Dictionary<UpgradeLine, Ingredient[][]> Costs = new Dictionary<UpgradeLine, Ingredient[][]>();

	private static bool _free;

	private static void Enable()
	{
		GameSession.OnLevelReady(Apply);
	}

	internal static bool IsSandbox()
	{
		Brix.Assets.Asset_Gamemode mode = GameParam.Gamemode;
		return mode != null && string.Equals(mode.shortCodename, "sandbox", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void Apply()
	{
		bool free = IsSandbox();
		if (free == _free)
		{
			return;
		}
		_free = free;
		if (free)
		{
			MakeFree();
		}
		else
		{
			Restore();
		}
		Plugin.Log.LogInfo("Upgrades: research is " + (free ? "free" : "paid") + " (gamemode " + ((GameParam.Gamemode != null) ? GameParam.Gamemode.shortCodename : "none") + ")");
	}

	private static void MakeFree()
	{
		foreach (UpgradeLine line in UpgradeLines.All)
		{
			Costs[line] = line.Cost;
			Ingredient[][] empty = new Ingredient[line.Cost.Length][];
			for (int tier = 0; tier < empty.Length; tier++)
			{
				empty[tier] = (line.Cost[tier] != null) ? new Ingredient[0] : null;
			}
			line.Cost = empty;
			foreach (RecipeInfo recipe in line.Recipe)
			{
				if (recipe == null || Ingredients.ContainsKey(recipe))
				{
					continue;
				}
				Ingredients[recipe] = (Description)IngredientsField.GetValue(recipe);
				IngredientsField.SetValue(recipe, new Description());
			}
		}
	}

	private static void Restore()
	{
		foreach (KeyValuePair<UpgradeLine, Ingredient[][]> pair in Costs)
		{
			pair.Key.Cost = pair.Value;
		}
		Costs.Clear();
		foreach (KeyValuePair<RecipeInfo, Description> pair in Ingredients)
		{
			IngredientsField.SetValue(pair.Key, pair.Value);
		}
		Ingredients.Clear();
	}
}
