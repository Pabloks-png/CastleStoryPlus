using BepInEx.Configuration;
using Brix.Game;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.WorkerAI;

// Workers go on to their next task sooner: the game ends every task with a fixed wait in AI ticks, 10 after a task
// done and 30 after a failed one, before the worker looks for work again. At the game's low tick rate, and more so
// when the frame rate drops, that was 1 to 1.5 seconds of standing still after every task and 4 to 6 seconds after a
// failed one (a path that could not be walked). A player's worker now waits [QuickNextTask] TicksAfterTask (2) and
// TicksAfterFailure (10). Only the game's default waits are shortened (combat and move orders set their own), and
// the enemies keep theirs. Host only (the tasks run on the host).
[Feature(Features.QuickNextTask, Features.QuickNextTaskInfo)]
[HarmonyPatch(typeof(Task), nameof(Task.WrapNode))]
internal static class QuickNextTask
{
	private const int GameAfterTask = 10;

	private const int GameAfterFailure = 30;

	private static ConfigEntry<int> _afterTask;

	private static ConfigEntry<int> _afterFailure;

	private static void Enable()
	{
		_afterTask = Plugin.Cfg.Bind("QuickNextTask", "TicksAfterTask", 2, new ConfigDescription("AI ticks a player's worker waits after a task before looking for the next (the game: 10).", new AcceptableValueRange<int>(0, 10)));
		_afterFailure = Plugin.Cfg.Bind("QuickNextTask", "TicksAfterFailure", 10, new ConfigDescription("AI ticks a player's worker waits after a failed task before looking for the next (the game: 30).", new AcceptableValueRange<int>(0, 30)));
	}

	private static void Prefix(Task __instance)
	{
		if (!(__instance.Worker is Labor labor))
		{
			return;
		}
		Faction faction = Faction.GetFaction(labor.gameObject);
		if (faction == null || faction.isAI)
		{
			return;
		}
		if (__instance.DelayOnFinished == GameAfterTask)
		{
			__instance.DelayOnFinished = _afterTask.Value;
		}
		if (__instance.DelayOnFail == GameAfterFailure)
		{
			__instance.DelayOnFail = _afterFailure.Value;
		}
	}
}
