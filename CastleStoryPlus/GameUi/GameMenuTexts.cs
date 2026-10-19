using System;
using System.Collections.Generic;
using Brix.Game;
using Brix.Game.AI;
using Brix.UI.Icons;
using CastleStoryPlus.LuaUi;
using UnityEngine;

namespace CastleStoryPlus.GameUi;

// Each task type's name, icon and icon colour (LUI/Meta/Meta_Project.lua). Features name or draw their own tasks
// through NameOverrides and IconOverrides (the base's fixed tasks, tree farms).
internal static class GameMenuTexts
{
	private sealed class TaskType
	{
		public IconKey Icon;

		public bool Yellow;

		public string Name;
	}

	private static readonly Dictionary<string, TaskType> Types = new Dictionary<string, TaskType>
	{
		{ GameUiState.Build, new TaskType { Icon = IconKeys._UI_Build, Name = "##task_build" } },
		{ GameUiState.Quarry, new TaskType { Icon = IconKeys._UI_Quarry, Name = "##task_quarry" } },
		{ GameUiState.Cleanup, new TaskType { Icon = IconKeys._UI_Cleanup, Name = "##task_cleanup" } },
		{ GameUiState.Harvest, new TaskType { Icon = IconKeys._UI_Pick, Yellow = true, Name = "##task_harvest" } },
		{ GameUiState.Repair, new TaskType { Icon = IconKeys._UI_Repair, Name = "##task_repair" } },
		{ GameUiState.Landscape, new TaskType { Icon = IconKeys._UI_Landscape, Name = "##task_landscape" } },
		{ GameUiState.Tunnel, new TaskType { Icon = IconKeys._UI_Tunnel, Name = "##task_tunnel" } },
		{ GameUiState.Idle, new TaskType { Icon = IconKeys._UI_CrystalOutline, Name = "##task_idle" } }
	};

	// A name for the task, or null for the game's.
	internal static readonly List<Func<Project, string>> NameOverrides = new List<Func<Project, string>>();

	// An icon for the task, or null for the game's.
	internal static readonly List<Func<Project, Sprite>> IconOverrides = new List<Func<Project, Sprite>>();

	internal static bool Known(string type)
	{
		return type != null && Types.ContainsKey(type);
	}

	internal static string TaskName(Project project)
	{
		if (project == null)
		{
			return string.Empty;
		}
		foreach (Func<Project, string> name in NameOverrides)
		{
			string found = name(project);
			if (found != null)
			{
				return found;
			}
		}
		return TypeName(GameUiState.TypeOf(project));
	}

	internal static string TypeName(string type)
	{
		return (type != null && Types.TryGetValue(type, out TaskType found)) ? GameUiState.Text(found.Name) : string.Empty;
	}

	internal static Sprite TaskIcon(Project project)
	{
		if (project == null)
		{
			return null;
		}
		foreach (Func<Project, Sprite> icon in IconOverrides)
		{
			Sprite found = icon(project);
			if (found != null)
			{
				return found;
			}
		}
		string type = GameUiState.TypeOf(project);
		return (type != null && Types.TryGetValue(type, out TaskType t)) ? GameUiState.Icon(t.Icon) : null;
	}

	internal static Color TaskIconColor(Project project)
	{
		string type = GameUiState.TypeOf(project);
		if (type == GameUiState.Idle)
		{
			Faction faction = (project != null) ? project.faction : null;
			return (faction != null) ? faction.FactionColor : LuiStyle.White;
		}
		return (type != null && Types.TryGetValue(type, out TaskType t) && t.Yellow) ? LuiStyle.CastleYellow : LuiStyle.White;
	}
}
