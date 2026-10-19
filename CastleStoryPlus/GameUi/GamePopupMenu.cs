using System;
using System.Collections.Generic;
using Brix.Engine.Nature;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.UI;
using Brix.Game.Components;
using Brix.Game.Components.Tunnel;
using Brix.Game.Network;
using Brix.Input;
using Brix.Lua;
using Brix.UI;
using Brix.UI.Icons;
using CastleStoryPlus.LuaUi;
using UnityEngine;

namespace CastleStoryPlus.GameUi;

// What features use of the task popup (the reloadable UI assembly builds it: GamePopupBuilder): the tooltip article
// of a button, the furnace's fuel shown in place of heat, the workshop settings section, and the workshop's queue
// commands.
internal static class GamePopupMenu
{
	// The tooltip article of each button (LUI/Article/Tooltip), as GameMenu's handles had in TooltipPath.
	private static readonly Dictionary<string, string> Articles = new Dictionary<string, string>
	{
		{ "BlueprintActive", "Task_Build_BlueprintActive" },
		{ "blueprintMove", "Task_Build_BlueprintMove" },
		{ "blueprintCopy", "Task_Build_BlueprintCopy" },
		{ "blueprintSplit", "Task_Build_BlueprintSplit" },
		{ "blueprintDelete", "Task_Build_BlueprintDelete" },
		{ "blueprintExpand", "Task_Build_BlueprintExpand" },
		{ "blueprintClear", "Task_Build_BlueprintClear" },
		{ "landscapeRaiseAll", "Task_Landscape_Raise" },
		{ "landscapeDigAll", "Task_Landscape_Lower" },
		{ "quarryStairs", "Task_Quarry_Stairs" },
		{ "quarryKeepStone", "Task_Quarry_KeepStone" },
		{ "quarryKeepIron", "Task_Quarry_KeepIron" },
		{ "quarryKeepBrimstone", "Task_Quarry_KeepBrimstone" },
		{ "quarryKeepCoal", "Task_Quarry_KeepBrimstone" },
		{ "quarryKeepBlueCrystal", "Task_Quarry_KeepBrimstone" },
		{ "tunnelDirUp", "Task_Tunnel_Up" },
		{ "tunnelDirStraight", "Task_Tunnel_Straight" },
		{ "tunnelDirDown", "Task_Tunnel_Down" },
		{ "tunnelKeepStone", "Task_Tunnel_KeepStone" },
		{ "tunnelKeepIron", "Task_Tunnel_KeepIron" },
		{ "tunnelKeepBrimstone", "Task_Tunnel_KeepBrimstone" },
		{ "tunnelKeepCoal", "Task_Tunnel_KeepBrimstone" },
		{ "tunnelKeepBlueCrystal", "Task_Tunnel_KeepBrimstone" },
		{ "harvestBoulder", "Task_Harvest_Boulders" },
		{ "harvestIron", "Task_Harvest_Iron" },
		{ "harvestBrimstone", "Task_Harvest_Brimstone" },
		{ "harvestTree", "Task_Harvest_Trees" },
		{ "harvestPlant", "Task_Harvest_Plants" },
		{ "priorityLower", "Task_All_LowerPriority" },
		{ "priorityRaise", "Task_All_RaisePriority" },
		{ "taskActionsOk", "Task_All_Ok" },
		{ "taskActionsApply", "Task_All_Apply" },
		{ "taskActionsCancel", "Task_All_Cancel" },
		{ "taskActionsDelete", "Task_All_Delete" },
		{ "idleTaskPopulationCount", "Task_Idle_Population" },
		{ "idleTaskActionsMortuary", "Task_Idle_Dead" },
		{ "idleTaskActionsSpawnUnit", "Task_Idle_SpawnUnit" },
		{ "toggleLoop", "Operable_Queue_ToggleLooping" },
		{ "clearQueue", "Operable_Queue_Clear" }
	};

	internal static string ArticleOf(string id)
	{
		return (id != null && Articles.TryGetValue(id, out string name)) ? name : null;
	}

	// count asked stays the recipe's.
	internal static Func<CraftingStation, LuaCrafting.LuaResource, LuaCrafting.LuaResource> ShownInput;

	internal static LuaCrafting.LuaResource Shown(LuaCrafting.LuaResource resource)
	{
		CraftingStation station = GameUiState.Station;
		return (ShownInput != null && station != null) ? ShownInput(station, resource) : resource;
	}

	// Rows under the recipes for the workshop's own settings (the mod's loop limit and priority), shown while a
	// workshop is selected and the section has any.
	internal static UiSection WorkshopSettings(UiPanel popup)
	{
		UiSection settings = popup.Find("operableSettings");
		if (settings == null)
		{
			settings = popup.SectionAfter("operableRecipes", "operableSettings", null);
			settings.Visible = () => GameUiState.Station != null && settings.Items.Count > 0;
		}
		return settings;
	}

	internal static LuaCrafting.LuaRecipe ActiveRecipe()
	{
		List<LuaCrafting.LuaRecipe> queue = CraftingCatalog.Queue(GameUiState.Station);
		return (queue.Count > 0) ? queue[0] : LuaCrafting.LuaRecipe.None;
	}

	internal static void AddToQueue(LuaCrafting.LuaRecipe recipe)
	{
		CraftingStation station = GameUiState.Station;
		if (station == null)
		{
			return;
		}
		CraftingCatalog.EnsureMapped();
		List<RecipeInfo> infos = LuaCrafting.ToRecipeInfo(recipe);
		if (infos == null)
		{
			return;
		}
		foreach (RecipeInfo info in infos)
		{
			CraftingStation.localUserProject.CallCmdcraftingQueueRecipe(station.gameObject, info.id, 1);
		}
	}

	internal static void ToggleLoop()
	{
		CraftingStation station = GameUiState.Station;
		if (station != null)
		{
			CraftingStation.localUserProject.CallCmdcraftingLoop(station.gameObject, !station.Looping);
		}
	}

	internal static void ClearQueue()
	{
		CraftingStation station = GameUiState.Station;
		if (station != null)
		{
			CraftingStation.localUserProject.CallCmdcraftingDoNothing(station.gameObject);
		}
	}
}
