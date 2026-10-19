using System;
using System.Collections.Generic;
using Brix.UI.Builder.Menu.Component;
using CastleStoryPlus.Core;
using CastleStoryPlus.LuaUi;
using HarmonyLib;

namespace CastleStoryPlus.GameUi;

// [LuaUi] CsGameMenu: the whole in-game menu in C#. GameMenu.lua (the scene's "Menu: Taskbar (Lua)", GS_Taskbar,
// loaded by every gamemode's ui.lua when the game starts) is not loaded at all: its menu component's Load starts
// the C# menu instead, and its Unload closes it. GameMenu built some 270 Lua menus (about 25 s of a map's loading)
// and kept a Lua script with all their tables alive; a Lua failure in it took every menu down.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
internal static class CsGameMenu
{
	internal const string LuaPath = "LUI/Menus/GameMenu.lua";

	// The menu of the game being played, or null.
	internal static GameMenuModel Model
	{
		get { return (UiAssembly.Model != null) ? UiAssembly.Model() : null; }
	}

	private static void Enable()
	{
		if (!LuaUiConfig.CsGameMenu)
		{
			return;
		}
		UiAssembly.Load();
		GameSession.OnLeave(Close);
		Plugin.Log.LogInfo("CsGameMenu: the in-game menu is C#; GameMenu.lua is not loaded");
	}

	internal static void Open()
	{
		if (UiAssembly.Open == null)
		{
			Plugin.Log.LogError("CsGameMenu: the UI assembly is not loaded; no in-game menu");
			return;
		}
		UiAssembly.Open(false);
	}

	internal static void Close()
	{
		UiAssembly.Close?.Invoke();
	}

	internal static bool IsGameMenu(LuaMenuComponent component)
	{
		return component != null && component.Path == LuaPath;
	}
}

// What features add to the C# in-game menu: called once the menu is built, each with the model, to add buttons,
// sections and panels where they belong; and hotkeys of the Projects mode (task actions, priority, recipes).
internal static class GameMenuItems
{
	private static readonly List<Action<GameMenuModel>> Extensions = new List<Action<GameMenuModel>>();

	// Keys active while the menu is in its Projects mode (nothing being built or chosen).
	internal static readonly Dictionary<string, Action> ProjectHotkeys = new Dictionary<string, Action>();

	// Keys active also while a catalogue is open (priority, delete task).
	internal static readonly Dictionary<string, Action> TaskHotkeys = new Dictionary<string, Action>();

	// While any says true, a blueprint put in the hand does not open its catalogue (MoveStructure: a building
	// being moved).
	internal static readonly List<Func<bool>> KeepCatalogueClosed = new List<Func<bool>>();

	internal static void Extend(Action<GameMenuModel> extension)
	{
		Extensions.Add(extension);
	}

	internal static void Apply(GameMenuModel model)
	{
		foreach (Action<GameMenuModel> extension in Extensions)
		{
			try
			{
				extension(model);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("CsGameMenu: a feature's menu items: " + ex);
			}
		}
	}
}

// GameMenu.lua's menu component starts the C# menu instead of loading the Lua file.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
[HarmonyPatch(typeof(LuaMenuComponent), nameof(LuaMenuComponent.Load))]
internal static class CsGameMenuLoadPatch
{
	private static bool Prepare()
	{
		return LuaUiConfig.CsGameMenu;
	}

	private static bool Prefix(LuaMenuComponent __instance, ref bool __result)
	{
		if (!CsGameMenu.IsGameMenu(__instance))
		{
			return true;
		}
		CsGameMenu.Open();
		__result = true;
		return false;
	}
}

[Feature(Features.CsMenus, Features.CsMenusInfo)]
[HarmonyPatch(typeof(LuaMenuComponent), nameof(LuaMenuComponent.Unload))]
internal static class CsGameMenuUnloadPatch
{
	private static bool Prepare()
	{
		return LuaUiConfig.CsGameMenu;
	}

	private static void Prefix(LuaMenuComponent __instance)
	{
		if (CsGameMenu.IsGameMenu(__instance))
		{
			CsGameMenu.Close();
		}
	}
}
