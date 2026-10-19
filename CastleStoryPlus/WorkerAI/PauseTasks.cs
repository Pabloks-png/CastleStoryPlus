using System.Collections.Generic;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Network;
using Brix.Lua;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.WorkerAI;

// A task can be paused instead of deleted: a pause button among the task's actions (next to delete) stops all work
// on it, and pressing it again resumes it. A paused task keeps its blueprints, zones and settings; no bricktron takes
// work from it, and those working on it when it is paused stop at once. Its crew are released to the free workers,
// so the task shows no one assigned, and are put back on it when it is resumed (those not given another task in
// the meantime; a crew released before the world was saved is not remembered after loading it). The pause is a spare bit of the task's own state flags, so it is synced to
// multiplayer clients and saved with the task by the game itself; clients send the change to the host.
[Feature(Features.PauseTasks, Features.PauseTasksInfo)]
internal static class PauseTasks
{
	// A bit of ProjectState.flags the game does not use (it uses 1 to 32; ProjectFlags keeps the others).
	internal const int PausedFlag = 64;

	private const int SetPausedHash = 1129595232;

	// Server: the crew each paused task released, put back when it is resumed.
	private static readonly Dictionary<Project, List<Labor>> Released = new Dictionary<Project, List<Labor>>();

	private static readonly AccessTools.FieldRef<ProjectState, int> Flags = AccessTools.FieldRefAccess<ProjectState, int>("flags");

	private static void Enable()
	{
		NetworkBehaviour.RegisterCommandDelegate(typeof(UNetProjectCmd), SetPausedHash, InvokeSetPaused);
		GameSession.OnLeave(Released.Clear);
		GameUi.GameMenuItems.Extend(AddToMenu);
		LuaInjection.AddFunction("TaskPaused", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			Project project = (args.Count > 0 && !args[0].IsNil()) ? args.AsComponent<Project>(0, "TaskPaused", allowNil: true) : null;
			return DynValue.NewBoolean(IsPaused(project));
		});
		LuaInjection.AddFunction("ToggleTaskPaused", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			Project project = (args.Count > 0 && !args[0].IsNil()) ? args.AsComponent<Project>(0, "ToggleTaskPaused", allowNil: true) : null;
			return DynValue.NewBoolean(Toggle(project));
		});
		// Among the task's actions, before delete.
		LuaInjection.AddPatch(Features.PauseTasks, "LUI/Menus/GameMenu.lua", "---task.actions.delete\ndo\n", LuaInjection.Mode.InsertBefore, @"---task.actions.pause (Castle Story Plus)
do
local h = MenuHandle.New()
local Paused = ||Data.Project:IsSelectedNotOfTypeIdle() and CastleStoryPlus.TaskPaused(Data.Project:GetSelected())
h.Visible = ||Data.Project:IsSelectedNotOfTypeIdle()
h.Label = function() if Paused() then return ""Resume task"" end return ""Pause task"" end
h.Icon = function() if Paused() then return IconKeys.UI_Play:Get64() end return IconKeys.UI_Pause:Get64() end
h.IconSize = ||32
h.IconColor = function() if Paused() then return _colorYellow end return CastleYellow end
h.Highlight = function() if Paused() then return HighlightMode.All end return HighlightMode.None end
h.ev_refresh = Event.New()
h.OnAction = function()
	if not UIGame.IsPaused() and Data.Project:IsSelectedNotOfTypeIdle() then
		CastleStoryPlus.ToggleTaskPaused(Data.Project:GetSelected())
	end
	h.ev_refresh:Invoke()
end

_m.onSetSelectedProject:AddListener(||h.ev_refresh:Invoke())
_m.onSetStatus:AddListener(||h.ev_refresh:Invoke())
_m.sh.taskActions.ev_onLoad:AddListener(function(m, sh) m.AddMenuHandleToggle(h, sh) end)

_m.mg.projectContext:AddChild(h)
_m.bh.taskActionsPause = h
end

");
	}

	// The C# in-game menu: among the task's actions, before delete.
	private static void AddToMenu(GameUi.GameMenuModel menu)
	{
		System.Func<bool> paused = () => GameUi.GameUiState.IsSelectedNotIdle && IsPaused(GameUi.GameUiState.Selected);
		menu.Popup.Find("taskActions").InsertBefore("taskActionsDelete", new GameUi.UiButton
		{
			Id = "taskActionsPause",
			Visible = () => GameUi.GameUiState.IsSelectedNotIdle,
			Label = () => paused() ? "Resume task" : "Pause task",
			Icon = () => GameUi.GameUiState.Icon(paused() ? Brix.UI.Icons.IconKeys.UI_Play : Brix.UI.Icons.IconKeys.UI_Pause),
			IconColor = () => LuaUi.LuiStyle.CastleYellow,
			Highlight = paused,
			OnClick = (bool shift, bool ctrl) =>
			{
				if (!GameUi.GameUiState.Paused && GameUi.GameUiState.IsSelectedNotIdle)
				{
					Toggle(GameUi.GameUiState.Selected);
				}
			}
		});
	}

	internal static bool IsPaused(Project project)
	{
		return project != null && project.State != null && (Flags(project.State) & PausedFlag) != 0;
	}

	internal static bool CanPause(Project project)
	{
		return project != null && project.State != null && !(project is IdleProject);
	}

	// Client or host: shown at once; the host's value follows.
	private static bool Toggle(Project project)
	{
		if (!CanPause(project) || User.LocalUser == null)
		{
			return false;
		}
		bool paused = !IsPaused(project);
		UNetProjectCmd cmd = User.LocalUser.GetComponent<UNetProjectCmd>();
		if (cmd == null || cmd.isServer)
		{
			SetPaused(project, paused);
			return true;
		}
		SetFlag(project.State, paused);
		NetworkWriter writer = new NetworkWriter();
		writer.Write((short)0);
		writer.Write((short)5);
		writer.WritePackedUInt32((uint)SetPausedHash);
		writer.Write(cmd.GetComponent<NetworkIdentity>().netId);
		writer.Write(project.gameObject);
		writer.Write(paused);
		cmd.SendCommandInternal(writer, 0, "CmdSetTaskPaused");
		return true;
	}

	private static void InvokeSetPaused(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			return;
		}
		GameObject go = reader.ReadGameObject();
		bool paused = reader.ReadBoolean();
		if (go == null || !Affiliation.IsOwnedBy(go, obj.gameObject))
		{
			return;
		}
		SetPaused(go.GetComponent<Project>(), paused);
	}

	// Server: blueprints placed in a paused build task resume it. A paused build task is easily missed (it shows no
	// crew, and the build button selects it again), and placing blueprints in it means the player wants them built.
	internal static void ResumeForNewWork(Project project)
	{
		if (!NetworkServer.active || !IsPaused(project))
		{
			return;
		}
		Plugin.Log.LogInfo("PauseTasks: " + project.name + " resumed, blueprints were placed in it");
		SetPaused(project, false);
	}

	// Server: sets the flag and, when pausing, stops everyone working on the task.
	private static void SetPaused(Project project, bool paused)
	{
		if (!CanPause(project) || IsPaused(project) == paused)
		{
			return;
		}
		SetFlag(project.State, paused);
		Plugin.Log.LogInfo("PauseTasks: " + project.name + (paused ? " paused" : " resumed"));
		if (paused)
		{
			StopWork(project);
			ReleaseCrew(project);
		}
		else
		{
			RehireCrew(project);
		}
	}

	// The crew go to the free workers, as the game's "remove all workers" does.
	private static void ReleaseCrew(Project project)
	{
		List<Labor> crew = new List<Labor>(project.Crew());
		foreach (Labor labor in crew)
		{
			labor.AssignToIdle();
			labor.Autonomy = AutonomyStatus.FreeAgent;
		}
		if (crew.Count > 0)
		{
			Released[project] = crew;
		}
	}

	// Back on the task, as the game's "assign" does: those still alive and not given another task meanwhile.
	private static void RehireCrew(Project project)
	{
		if (!Released.TryGetValue(project, out List<Labor> crew))
		{
			return;
		}
		Released.Remove(project);
		foreach (Labor labor in crew)
		{
			if (labor.IsNullOrReleased() || (labor.Project != null && !(labor.Project is IdleProject)))
			{
				continue;
			}
			if (project.IsFull())
			{
				project.MaximumCrewSize++;
			}
			if (project.Hire(labor))
			{
				labor.Autonomy = AutonomyStatus.LockedToOperable;
			}
		}
	}

	private static void SetFlag(ProjectState state, bool paused)
	{
		int flags = Flags(state);
		int next = paused ? (flags | PausedFlag) : (flags & ~PausedFlag);
		if (next == flags)
		{
			return;
		}
		if (NetworkServer.active)
		{
			state.Networkflags = next;
		}
		else
		{
			Flags(state) = next;
		}
	}

	// Its crew, and any other worker whose current task came from one of its goals (helpers from other tasks).
	private static void StopWork(Project project)
	{
		project.CancelAllWork();
		foreach (Labor labor in Live<Labor>.Active())
		{
			GameObject giver = (labor.CurrentTask != null) ? labor.CurrentTask.Giver : null;
			if (giver != null && giver.GetComponentInParent<Project>() == project && labor.Project != project)
			{
				labor.AbortWorkSignal.Invoke(TaskResult.ProjectCancelledTask);
			}
		}
	}
}

// No worker takes work from a paused task: it is left out of the tasks a worker looks at.
[Feature(Features.PauseTasks, Features.PauseTasksInfo)]
[HarmonyPatch(typeof(Labor), nameof(Labor.RefreshAvailableProjects))]
internal static class PauseTasksAvailablePatch
{
	[HarmonyPriority(Priority.Last)]
	private static void Postfix(List<IOperable<Labor>> projects)
	{
		if (projects != null)
		{
			projects.RemoveAll((IOperable<Labor> operable) => operable is Project project && PauseTasks.IsPaused(project));
		}
	}
}

// A blueprint placed in a paused build task resumes it.
[Feature(Features.PauseTasks, Features.PauseTasksInfo)]
[HarmonyPatch(typeof(UNetBlueprint), "_FinishSpawnBlueprint")]
internal static class PauseTasksResumeOnPlacePatch
{
	private static void Postfix(Blueprint blueprint, bool instant)
	{
		if (!instant && blueprint != null && !blueprint.Released)
		{
			PauseTasks.ResumeForNewWork(blueprint.Project);
		}
	}
}

// A demolition placed in a paused build task resumes it.
[Feature(Features.PauseTasks, Features.PauseTasksInfo)]
[HarmonyPatch(typeof(UNetBlueprint), nameof(UNetBlueprint.CmdPlaceAnti))]
internal static class PauseTasksResumeOnDemolishPatch
{
	private static void Postfix(bool instant, GameObject BuildProject)
	{
		if (!instant && BuildProject != null)
		{
			PauseTasks.ResumeForNewWork(BuildProject.GetComponent<Project>());
		}
	}
}
