using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Beacons;
using Brix.Game.Components;
using Brix.Game.Network;
using Brix.Input;
using Brix.Legacy;
using Brix.Lua;
using CastleStoryPlus.Core;
using CastleStoryPlus.Giant;
using HarmonyLib;
using MoonSharp.Interpreter;
using Smooth.Slinq;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.WorkerAI;

// Cleaning up and repairing round the base are two fixed tasks in the task list, instead of chores every idle worker
// did on its own: a "Cleanup" task (loose items) and a "Repair" task (damaged blocks), each the game's own zone task
// centred on the home crystal. They have the task list's priority (Shift / Ctrl) and crew (+ / -, assign by dragging)
// like any task, so the player chooses how many bricktrons do them and how urgent they are; while they exist the
// idle workers no longer clean up or repair by themselves (AutoCleanup / AutoRepair). The zone's radius is the
// range of the crystal's AutoCleanup / AutoRepair setting (the crystal's + and -, or dragging the zone's edge, which
// changes the setting); the zone stays on the crystal. The tasks cannot be deleted and are made again if they are
// missing (a new world, an old save). They are ordinary cleanup and repair zones with one more bit in their state
// flags, so a save stays loadable without the mod (where they are plain zones). Host only, for the host's faction.
[Feature(Features.BaseJobs, Features.BaseJobsInfo)]
internal static class BaseJobs
{
	// A bit of ProjectState.flags no one else uses (the game 1 to 32, PauseTasks 64, WorkLimit 128 to 3968).
	internal const int FixedFlag = 4096;

	private const float CheckSeconds = 2f;

	// Without the AutoCleanup / AutoRepair setting (feature off): the zone's radius.
	private const int DefaultRadius = 15;

	private static bool _enabled;

	private static float _nextCheck;

	// The radius each task was last given, to tell the player's drag of its edge from the setting's change.
	private static readonly Dictionary<Project, int> GivenRadius = new Dictionary<Project, int>();

	internal const string CleanupName = "Auto cleanup";

	internal const string RepairName = "Auto repair";

	private static void Enable()
	{
		_enabled = true;
		GameUi.GameMenuTexts.NameOverrides.Add(NameOf);
		// The fixed tasks are named for what they are in the task list and over the selected task; other cleanup and
		// repair tasks keep the game's names. The flag is synced, so clients show the names too.
		LuaInjection.AddFunction("FixedTaskName", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			Project project = (args.Count > 0 && !args[0].IsNil()) ? args.AsComponent<Project>(0, "FixedTaskName", allowNil: true) : null;
			string name = NameOf(project);
			return (name != null) ? DynValue.NewString(name) : DynValue.Nil;
		});
		LuaInjection.AddPatch(Features.BaseJobs, "LUI/Menus/Game/Menu_TaskList.lua", "\t\treturn Data.Project:GetName(project.type)\n\tend\n\n_data.GetProjectPrioritySprite", LuaInjection.Mode.Replace, "\t\treturn CastleStoryPlus.FixedTaskName(project) or Data.Project:GetName(project.type)\n\tend\n\n_data.GetProjectPrioritySprite");
		LuaInjection.AddPatch(Features.BaseJobs, "LUI/Menus/GameMenu.lua", "h.LabelText = ||Data.Project:GetName(Data.Project:GetSelectedType())", LuaInjection.Mode.Replace, "h.LabelText = ||CastleStoryPlus.FixedTaskName(Data.Project:GetSelected()) or Data.Project:GetName(Data.Project:GetSelectedType())");
		GameSession.OnLeave(() =>
		{
			_nextCheck = 0f;
			GivenRadius.Clear();
		});
	}

	// The fixed task's name, or null for any other task.
	internal static string NameOf(Project project)
	{
		if (!IsFixed(project))
		{
			return Nature.TreeFarm.NameOf(project);
		}
		return (project is CleanupProject) ? CleanupName : (project is RepairProject) ? RepairName : null;
	}

	internal static bool IsFixed(Project project)
	{
		return project != null && project.State != null && (ProjectFlags.Get(project.State) & FixedFlag) != 0;
	}

	// Whether the faction's fixed task takes over the idle workers' chore.
	internal static bool Replaces(Faction faction, bool cleanup)
	{
		return _enabled && faction != null && Find(faction, cleanup) != null;
	}

	private static Project Find(Faction faction, bool cleanup)
	{
		List<IOperable<Labor>> projects = new List<IOperable<Labor>>();
		ProjectDatabase.For(faction).Projects().AddTo(projects);
		foreach (IOperable<Labor> operable in projects)
		{
			if (operable is Project project && project.IsStillAlive && IsFixed(project) && (cleanup ? project is CleanupProject : project is RepairProject))
			{
				return project;
			}
		}
		return null;
	}

	// Every frame on the host; does its work every few seconds.
	internal static void Update()
	{
		if (!NetworkServer.active || Time.time < _nextCheck || InputModeController.DefaultMode == InputMode.worldEditor)
		{
			return;
		}
		_nextCheck = Time.time + CheckSeconds;
		User user = User.LocalUser;
		Faction faction = (user != null) ? user.faction : null;
		if (faction == null)
		{
			return;
		}
		FireflyNest home = GiantBricktron.HomeNest(faction);
		if (home == null)
		{
			return;
		}
		XYZ center = XYZ.FromVector3(home.transform.position);
		Ensure(user, faction, center, cleanup: true, AutoCleanup.Radius);
		Ensure(user, faction, center, cleanup: false, AutoRepair.Radius);
	}

	private static void Ensure(User user, Faction faction, XYZ center, bool cleanup, ConfigEntry<int> setting)
	{
		Project project = Find(faction, cleanup);
		if (project == null)
		{
			project = Create(user, faction, center, cleanup);
			if (project == null)
			{
				return;
			}
		}
		BeaconDriver driver = project.GetComponent<BeaconDriver>();
		Beacon beacon = (driver != null) ? driver.Beacon : null;
		if (beacon == null)
		{
			return;
		}
		if (beacon.MaxRadius < BaseRanges.Max)
		{
			beacon.MaxRadius = BaseRanges.Max;
		}
		if (project.MaxSize < BaseRanges.Max)
		{
			project.MaxSize = BaseRanges.Max;
		}
		int wanted = (setting != null) ? BaseRanges.Clamped(setting) : DefaultRadius;
		int current = (int)beacon.radius;
		// The player dragged the zone's edge: that is the new range.
		if (setting != null && GivenRadius.TryGetValue(project, out int given) && current != given && current >= BaseRanges.Min)
		{
			setting.Value = Mathf.Clamp(current, BaseRanges.Min, BaseRanges.Max);
			wanted = BaseRanges.Clamped(setting);
		}
		if (current != wanted)
		{
			beacon.radius = wanted;
			driver.NetworkNetworkRadius = wanted;
		}
		GivenRadius[project] = wanted;
		if (beacon.position != center)
		{
			beacon.position = center;
			driver.NetworkNetworkPosition = center.ToVector3();
		}
	}

	// As the task bar's "new cleanup / repair task" and placing its marker on the crystal do.
	private static Project Create(User user, Faction faction, XYZ center, bool cleanup)
	{
		UNetGroupe groupe = user.GetComponent<UNetGroupe>();
		if (groupe == null)
		{
			return null;
		}
		// Only when a task is made (a new world, an old save): looking through the scene is fine here.
		HashSet<Project> before = new HashSet<Project>();
		foreach (Project project in Object.FindObjectsOfType<Project>())
		{
			before.Add(project);
		}
		groupe.CmdSpawnGoupe("Groupes", cleanup ? "Cleanup" : "Repair", center.ToVector3(), Quaternion.identity, false);
		Project made = null;
		foreach (Project project in Object.FindObjectsOfType<Project>())
		{
			if (!before.Contains(project) && project.faction == faction && (cleanup ? project is CleanupProject : project is RepairProject))
			{
				made = project;
				break;
			}
		}
		if (made == null || made.State == null)
		{
			Plugin.Log.LogWarning("BaseJobs: the game made no " + (cleanup ? "cleanup" : "repair") + " task");
			return null;
		}
		ProjectFlags.Set(made.State, ProjectFlags.Get(made.State) | FixedFlag);
		groupe.CmdCommit(made.gameObject);
		Plugin.Log.LogInfo("BaseJobs: made the base " + (cleanup ? "cleanup" : "repair") + " task at " + center);
		return made;
	}
}

// The fixed tasks cannot be deleted (the task's delete button, Ctrl+Del).
[Feature(Features.BaseJobs, Features.BaseJobsInfo)]
[HarmonyPatch(typeof(UNetGroupe), nameof(UNetGroupe.CmdDelete))]
internal static class BaseJobsNoDeletePatch
{
	private static bool Prefix(GameObject instance)
	{
		Project project = (instance != null) ? instance.GetComponent<Project>() : null;
		return !BaseJobs.IsFixed(project);
	}
}

[Feature(Features.BaseJobs, Features.BaseJobsInfo)]
[HarmonyPatch(typeof(InputModeController), nameof(InputModeController.Update))]
internal static class BaseJobsUpdatePatch
{
	private static void Prefix()
	{
		try
		{
			BaseJobs.Update();
		}
		catch (System.Exception ex)
		{
			Plugin.Log.LogWarning("BaseJobs: " + ex.Message);
		}
	}
}
