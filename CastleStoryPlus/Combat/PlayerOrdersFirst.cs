using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Combat;

// A player's order always wins over what a bricktron decides itself. A bricktron holds one next task (the pending
// one), and every order simply replaced it: in a fight the instinct ("an enemy is near, attack it") sends a new attack
// every few ticks, which threw out the player's order before it began, or cut it off once it ran; and an order given
// during a stroke that may not be broken off waited behind the fight. Now, while the player's order waits or runs:
// - the instinct does not send the bricktron to fight;
// - orders of its own or of its task (anything but another player order) do not replace it;
// - a task of its own that may not be interrupted (a stroke, a shot) is broken off, so the order starts at once.
// The enemy waves keep the game's behaviour.
[Feature(Features.PlayerOrdersFirst, Features.PlayerOrdersFirstInfo)]
internal static class PlayerOrdersFirst
{
	// Whether the bricktron has a player's order waiting or running.
	internal static bool HasPlayerOrder(Labor labor)
	{
		return IsPlayerTask(labor.CurrentTask) || IsPlayerTask(labor.PendingTask);
	}

	internal static bool IsPlayerTask(Task task)
	{
		return task != null && task.InitialPackage != null && task.InitialPackage.DecisionSource == Labor.DecisionSource.User
			&& (task.Status == TaskProgression.NotStarted || task.Status == TaskProgression.Running);
	}

	internal static bool IsPlayers(Labor labor)
	{
		return labor != null && labor.faction != null && !labor.faction.isAI;
	}
}

// The instinct does not send a bricktron to fight while it carries out a player's order.
[Feature(Features.PlayerOrdersFirst, Features.PlayerOrdersFirstInfo)]
[HarmonyPatch(typeof(Labor), "HasToFight")]
internal static class PlayerOrdersFirstFightPatch
{
	private static bool Prefix(Labor __instance, ref bool __result)
	{
		if (PlayerOrdersFirst.IsPlayers(__instance) && PlayerOrdersFirst.HasPlayerOrder(__instance))
		{
			__result = false;
			return false;
		}
		return true;
	}
}

// Orders of the bricktron itself or of its task do not replace a player's order; a player's order breaks off a task
// of the bricktron's own that would otherwise not let it start.
[Feature(Features.PlayerOrdersFirst, Features.PlayerOrdersFirstInfo)]
[HarmonyPatch(typeof(Labor), nameof(Labor.SendOrder))]
internal static class PlayerOrdersFirstSendPatch
{
	private static bool Prefix(Labor __instance, Task task, Labor.DecisionSource source)
	{
		if (!PlayerOrdersFirst.IsPlayers(__instance))
		{
			return true;
		}
		if ((source == Labor.DecisionSource.Character || source == Labor.DecisionSource.Project) && PlayerOrdersFirst.HasPlayerOrder(__instance))
		{
			Plugin.Log.LogDebug("PlayerOrdersFirst: " + __instance.name + " keeps the player's order over '" + (task?.Description ?? "?") + "' (" + source + ")");
			return false;
		}
		return true;
	}

	private static void Postfix(Labor __instance, Labor.DecisionSource source)
	{
		if (source != Labor.DecisionSource.User || !PlayerOrdersFirst.IsPlayers(__instance))
		{
			return;
		}
		Task current = __instance.CurrentTask;
		if (current != null && current != __instance.PendingTask && !current.CanInterrupt && current.Status == TaskProgression.Running && !PlayerOrdersFirst.IsPlayerTask(current))
		{
			__instance.AbortWorkSignal.Invoke(TaskResult.ReceivedNewInstruction);
		}
	}
}
