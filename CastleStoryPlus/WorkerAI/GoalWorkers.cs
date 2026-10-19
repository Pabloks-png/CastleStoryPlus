using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// The game's list of the workers on a task (Goal.workers) only loses a worker when its task for the goal ends. A task
// that never started never ends: a worker's next task (pending) is replaced when another order comes first (a raid's
// call to arms, a player's order, the next search) or dropped when its work is aborted, and the worker then stayed on
// the goal's list for good. Such a stale entry made the task look taken: a blueprint counted as already supplied
// ("Already enough workers doing this"), a dig, tree or repair task as crowded, so free workers passed it by and
// blueprints were never built (the worker trace showed build and dig tasks held by workers that were fighting or
// doing something else for minutes). Before a worker weighs a task, the workers on its list that neither work on it
// nor are about to start it are taken off.
internal static class GoalWorkers
{
	// The task being weighed by the running search (for the worker trace).
	[System.ThreadStatic]
	internal static Goal Evaluating;

	// Whether the worker's current or next task comes from the goal.
	internal static bool IsOn(Goal goal, Labor labor)
	{
		if (goal == null || labor.IsNullOrReleased())
		{
			return false;
		}
		GameObject giver = goal.gameObject;
		Task current = labor.CurrentTask;
		Task pending = labor.PendingTask;
		return (current != null && current.Giver == giver) || (pending != null && pending.Giver == giver);
	}

	// Takes the workers that left the goal without ending a task for it off its list.
	internal static void Prune(Goal goal)
	{
		List<Labor> workers = goal.workers;
		for (int i = workers.Count - 1; i >= 0; i--)
		{
			if (!IsOn(goal, workers[i]))
			{
				workers.RemoveAt(i);
			}
		}
	}
}

// While searching: the task's list of workers is cleaned before the task is weighed.
[Feature(Features.TaskReservation, Features.TaskReservationInfo)]
[HarmonyPatch(typeof(GoalSelector), nameof(GoalSelector.ProcessGoalAndReturnIsBest))]
internal static class GoalWorkersPrunePatch
{
	[HarmonyPriority(Priority.First)]
	private static void Prefix(Goal goal)
	{
		if (goal.IsNullOrReleased())
		{
			return;
		}
		GoalWorkers.Prune(goal);
		GoalWorkers.Evaluating = goal;
	}

	private static void Finalizer()
	{
		GoalWorkers.Evaluating = null;
	}
}
