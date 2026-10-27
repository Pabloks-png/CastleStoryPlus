using System.Collections.Generic;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Economy;

// A finished order always leaves the workshop's queue. The game takes it out by reference (the queue entry must be the
// very object the work was started from); when the queue had been read again from the workshop's synced ids in the
// meantime, the entry was another object for the same recipe, nothing was removed, and a queue that did not loop made
// the same order again and again (lantern wards without end). The entry is now matched by its recipe id when the
// object itself is not in the queue, and a line in the log says so.
[Feature(Features.CraftFinishFix, Features.CraftFinishFixInfo)]
[HarmonyPatch(typeof(CraftingLabor), nameof(CraftingLabor.FinishRecipe))]
internal static class CraftFinishFix
{
	private static void Prefix(CraftingLabor __instance, Recipe recipe)
	{
		RecipeInfo info = (recipe != null) ? recipe.Info : null;
		List<RecipeInfo> queue = __instance.QueuedRecipes;
		if (info == null || queue == null || queue.Contains(info))
		{
			return;
		}
		for (int i = 0; i < queue.Count; i++)
		{
			if (queue[i] != null && queue[i].id == info.id)
			{
				queue[i] = info;
				Plugin.Log.LogInfo("CraftFinishFix: " + __instance.name + " finished recipe " + info.id + ", its queue entry was another object; removed by id");
				return;
			}
		}
		Plugin.Log.LogInfo("CraftFinishFix: " + __instance.name + " finished recipe " + info.id + ", which is not in its queue");
	}
}
