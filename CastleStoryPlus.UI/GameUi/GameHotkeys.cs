using System;
using System.Collections.Generic;
using Brix.Input;
using Brix.UI.Builder.Menu;
using Brix.Utils.UI;
using CastleStoryPlus.LuaUi;
using Rewired;

namespace CastleStoryPlus.GameUi;

// The menu's hotkeys as GameMenu.lua set them up, by input mode: Projects (the menu is open and nothing is being
// built: call to arms, the catalogues, recipes, task actions, priority...), Tasks (the new-task menu is open),
// Bricks and Structures (a catalogue is open: its blueprints' keys) and Blueprints (selecting blueprints). The keys
// go through the game's own KeyBindingsUtility (Rewired actions), so the player's bindings apply.
internal static class GameHotkeys
{
	private static readonly HashSet<string> Registered = new HashSet<string>();

	internal static void Register(string action, Action run)
	{
		if (string.IsNullOrEmpty(action) || run == null)
		{
			return;
		}
		KeyBindingsUtility.RegisterAction(action, (InputActionEventData data) =>
		{
			try
			{
				run();
				GameUiState.Announce();
			}
			catch (Exception ex)
			{
				LuaBridge.LogOnce("GameUi hotkey " + action, ex.ToString());
			}
		});
		Registered.Add(action);
	}

	internal static void Unregister(string action)
	{
		KeyBindingsUtility.UnregisterAction(action);
		Registered.Remove(action);
	}

	// Every key the menu registered (the menu is closing).
	internal static void UnregisterAll()
	{
		foreach (string action in Registered)
		{
			KeyBindingsUtility.UnregisterAction(action);
		}
		Registered.Clear();
		SetCombatHotkeys(false);
	}

	internal static void SetCategory(KeyBindingsUtility.Category category, bool on)
	{
		LuaMenuUtility.SetKeyBindingsTo(category, on);
	}

	internal static void ResetCategories()
	{
		LuaMenuUtility.ResetAllKeyBindings();
	}

	// The selected bricktrons' stance, stop and jump keys (fy_Bricktron.ToggleHotkeys).
	internal static void SetCombatHotkeys(bool on)
	{
		CombatHotkeys.hotkeysActivated = on;
	}
}
