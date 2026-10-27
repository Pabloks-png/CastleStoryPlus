using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Pathfinding.Test;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// A worker that finishes a task of a work area goes on to the area's next task at once. The game let it stand
// "available" for 1 to 1.5 seconds after every block dug, tree felled or item cleaned up: its search for the next task
// walks through every project, waits a tick per project and per resource it checks, and shares a small time budget
// per frame with every other worker's search. Now the moment a task of a quarry, tunnel, dig, flatten, harvest, cleanup or build
// area ends well, the area's next task is chosen right there with the game's own choice (GoalSelector, so the mod's
// reservations and reach checks count too) and given to the worker as its next task. When the area has nothing the
// worker can do, the game's search goes on as before. A free worker goes by priority over every area: it stays in an
// area only while the game's own search chose that area for it in the last RecheckSeconds (that search goes through
// the areas by priority, so nothing better had work for it); then the game searches once more. A build task's next blueprint
// comes from the build task's own search (what the worker can fetch, top priority first), run in one go. A workshop's
// worker gets its next step from the workshop at once (fetch ingredients, work, take out what is made). The worker trace logs each prediction. Host only, the player's workers.
[Feature(Features.TaskPrediction, Features.TaskPredictionInfo)]
[HarmonyPatch(typeof(Task), "OnFinally")]
internal static class TaskPrediction
{
	// Most tasks one prediction looks at; larger areas leave it to the game's time-sliced search.
	private const int MaxGoals = 1500;

	// Most steps of one search (a build search's route search steps included).
	private const int MaxSteps = 5000;

	// Most CPU time one search may take; a longer one is left to the game's search spread over frames.
	private const double MaxMilliseconds = 4.0;

	private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();

	private static readonly List<Goal> Goals = new List<Goal>();

	// How long a free worker keeps to the area the game's search chose for it before the game searches again.
	private const float RecheckSeconds = 30f;

	// The area the game's own search last gave each worker a task of, and when.
	private static readonly Dictionary<Labor, KeyValuePair<Project, float>> SearchChose = new Dictionary<Labor, KeyValuePair<Project, float>>();

	private static bool _busy;

	// The task each worker was last given by a prediction (RouteView colours its route).
	private static readonly Dictionary<Labor, Task> Predicted = new Dictionary<Labor, Task>();

	private static void Enable()
	{
		GameSession.OnLeave(() =>
		{
			Predicted.Clear();
			SearchChose.Clear();
		});
	}

	internal static bool IsPredicted(Labor labor)
	{
		return labor != null && Predicted.TryGetValue(labor, out Task task) && task != null && (labor.CurrentTask == task || labor.PendingTask == task);
	}

	private static void Prefix(Task __instance)
	{
		if (_busy || __instance.result != TaskResult.Succes || !(__instance.Worker is Labor labor) || labor.IsNullOrReleased())
		{
			return;
		}
		if (labor.faction == null || labor.faction.isAI || !UnityEngine.Networking.NetworkServer.active)
		{
			return;
		}
		if (__instance.InitialPackage == null || __instance.InitialPackage.DecisionSource != Labor.DecisionSource.Project)
		{
			Trace(labor, "next task: not predicted, the task came from " + ((__instance.InitialPackage != null) ? __instance.InitialPackage.DecisionSource.ToString() : "nowhere"));
			return;
		}
		// A workshop's worker: the workshop decides at once what comes next (fetch, work, take out what is made).
		if (labor.Operable is CraftingStation station && station.Operator == labor)
		{
			if (labor.PendingTask == null)
			{
				Guarded(() => PredictWorkshop(labor, station));
			}
			return;
		}
		GameObject target = (__instance.Target != null) ? __instance.Target : __instance.Giver;
		Goal finished = (target != null) ? target.GetComponent<Goal>() : null;
		Project project = (finished != null && finished.project != null) ? finished.project : labor.Project;
		bool predicted = Predicted.TryGetValue(labor, out Task last) && last == __instance;
		if (!predicted && project != null)
		{
			SearchChose[labor] = new KeyValuePair<Project, float>(project, (__instance.TaskStartedTimestamp > 0f) ? __instance.TaskStartedTimestamp : Time.time);
		}
		if (!Predictable(project))
		{
			Trace(labor, "next task: not predicted for " + ((project != null) ? project.GetType().Name : "no task area"));
			return;
		}
		if (labor.PendingTask != null)
		{
			Trace(labor, "next task: already has one (" + labor.PendingTask.Description + ")");
			return;
		}
		_busy = true;
		try
		{
			Predict(labor, project);
		}
		catch (System.Exception ex)
		{
			Plugin.Log.LogWarning("TaskPrediction: " + ex.Message);
		}
		finally
		{
			_busy = false;
		}
	}

	private static void Guarded(System.Action action)
	{
		_busy = true;
		try
		{
			action();
		}
		catch (System.Exception ex)
		{
			Plugin.Log.LogWarning("TaskPrediction: " + ex.Message);
		}
		finally
		{
			_busy = false;
		}
	}

	// As the game asks a workshop for its worker's next task (CraftingStation.GetInstruction, AssignInstruction).
	private static void PredictWorkshop(Labor labor, CraftingStation station)
	{
		string name = station.GetType().Name;
		if (!station.IsStillAlive || !station.HasWorkFor(labor))
		{
			Trace(labor, "next task: nothing to do at " + name + ", the game searches");
			return;
		}
		CraftingStation.Actions action = station.DecideWhatToDo(labor);
		OrderPackage<Labor> package = station.GetPackage(action);
		if (package == null || package == OrderPackage<Labor>.Empty || package.Instruction == null)
		{
			Trace(labor, "next task: nothing to do at " + name + ", the game searches");
			return;
		}
		if (!station.Hire(labor))
		{
			Trace(labor, "next task: " + name + " would not take it on");
			return;
		}
		package.Send(labor);
		Predicted[labor] = labor.PendingTask;
		Trace(labor, "next task predicted: " + action + " at " + name);
	}

	private static bool Predictable(Project project)
	{
		return project != null && project.IsStillAlive
			&& (project is MineProject || project is TunnelProject || project is LandscapeProject || project is HarvestProject || project is CleanupProject || project is BuildProject);
	}

	private static void Predict(Labor labor, Project project)
	{
		if (!project.ReadyToAssignGoals() || !project.HasWorkFor(labor))
		{
			Trace(labor, "next task: " + project.GetType().Name + " has no work for it");
			return;
		}
		if (!KeepsToArea(labor, project))
		{
			Trace(labor, "next task: the game searches again (priorities)");
			return;
		}
		GoalSelector selector = project.goalSelector;
		if (selector == null)
		{
			return;
		}
		IEnumerator<Goal> candidates = Candidates(labor, project);
		if (candidates == null)
		{
			return;
		}
		GoalSelector.GoalSelectionData data = GoalSelector.GoalSelectionData.Get();
		Goal best;
		try
		{
			// A build search yields null where the game would wait a tick (its route search is spread over ticks):
			// here it just goes on, up to MaxSteps.
			Clock.Reset();
			Clock.Start();
			for (int step = 0; step < MaxSteps && candidates.MoveNext(); step++)
			{
				if (Clock.Elapsed.TotalMilliseconds > MaxMilliseconds)
				{
					Trace(labor, "next task: the search in " + project.GetType().Name + " takes too long, the game searches");
					return;
				}
				Goal goal = candidates.Current;
				if (goal == null || goal.IsNullOrReleased() || !goal.IsValid())
				{
					continue;
				}
				if (selector.ProcessGoalAndReturnIsBest(labor, goal, project, data))
				{
					break;
				}
			}
			best = data.BestGoal;
			// Reserves the goal for this worker (TaskReservation), as the game's search does when it ends.
			if (best == null || selector.GoalIsRejected(labor, best, data.foundUnmapped))
			{
				Trace(labor, "next task: nothing in " + project.GetType().Name + " for it, the game searches");
				return;
			}
			selector.errorStore.Reset(labor);
		}
		finally
		{
			data.Dispose();
			candidates.Dispose();
		}
		// As the game gives a project's task (Project.GetInstruction, AssignInstruction).
		if (!project.GetPrepareFunction(best)(labor))
		{
			Trace(labor, "next task: " + project.GetType().Name + " would not take it on");
			return;
		}
		best.Package(Labor.DecisionSource.Project).Send(labor);
		Predicted[labor] = labor.PendingTask;
		Trace(labor, "next task predicted: " + best.name + " in " + project.GetType().Name);
	}

	// The tasks the project's own search would look at, in its order: a build task's blueprints the worker can bring
	// something to (with the blueprints given top priority first, BuildPriority), the other areas' tasks.
	private static IEnumerator<Goal> Candidates(Labor labor, Project project)
	{
		if (project is BuildProject build)
		{
			TestPathfinding pathfinding = TestPathfinding.Instance;
			if (pathfinding == null || pathfinding.DirtyVoxels.Count > 0 || pathfinding.QueuedVoxels.Count > 0)
			{
				Trace(labor, "next task: routes are being updated, the game searches");
				return null;
			}
			List<BuildGoal> first = Building.BuildPriority.Of(build);
			BuildGoalProvider.FulfillableEnumerator rest = build.goals.FulfillableBy(labor);
			return (first != null) ? new Building.PriorityFirstEnumerator(first, rest) : (IEnumerator<Goal>)rest;
		}
		Goals.Clear();
		foreach (IGoalProvider provider in project.GetGoalProviders())
		{
			provider?.GetAll(Goals);
		}
		if (Goals.Count == 0 || Goals.Count > MaxGoals)
		{
			Trace(labor, "next task: " + Goals.Count + " tasks in " + project.GetType().Name + ", left to the game");
			return null;
		}
		return new List<Goal>(Goals).GetEnumerator();
	}

	private static void Trace(Labor labor, string text)
	{
		if (Diagnostics.WorkerTrace.Active)
		{
			Diagnostics.WorkerTrace.Log(labor.name, text);
		}
	}

	// A worker of the area's crew keeps to it. A free worker only while the game's own search chose this area for it
	// a short while ago.
	private static bool KeepsToArea(Labor labor, Project project)
	{
		if (labor.Autonomy != AutonomyStatus.FreeAgent)
		{
			return true;
		}
		return SearchChose.TryGetValue(labor, out KeyValuePair<Project, float> chose) && chose.Key == project && Time.time - chose.Value <= RecheckSeconds;
	}
}
