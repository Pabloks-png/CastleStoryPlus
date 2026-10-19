using System.Collections.Generic;
using Brix.Lua;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;

namespace CastleStoryPlus.Memory;

// A Lua closure connected to one of the game's C# hooks (Project selected, crew info, ...) is only disconnected
// when its script dies if that script has hk_OnScriptKilled in its globals (LuaHookBase.HookDisconnectWhen). The
// scripts of the Lua menu components have none: the game never killed them, so their closures stayed alive. Since
// GameLeakFixes closes those scripts (LuaMenuScriptPatch), their closures die while still connected, and in the next
// game the first dead one throws "Attempting to Call on dead Closure", which ends the whole hook call: GameMenu's own
// listener after it never ran, so selecting a new build project no longer opened the Structures and Blocks menus.
// Here a closure whose script has no hk_OnScriptKilled is disconnected from its hook when its script is killed.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(LuaHookBase), "HookDisconnectWhen")]
internal static class LuaHookKillPatch
{
	private struct Connection
	{
		public LuaHookBase Hook;

		public Closure Act;
	}

	private static readonly Dictionary<Script, List<Connection>> Connections = new Dictionary<Script, List<Connection>>();

	private static void Postfix(LuaHookBase hook, Closure act)
	{
		Script script = act?.OwnerScript;
		if (script == null || hook == null)
		{
			return;
		}
		// The game disconnects these itself.
		if (script.Globals.Get("hk_OnScriptKilled").ToObject<LuaHook>() != null)
		{
			return;
		}
		if (!Connections.TryGetValue(script, out List<Connection> list))
		{
			list = new List<Connection>();
			Connections[script] = list;
			script.OnScriptKilled += OnScriptKilled;
		}
		list.Add(new Connection { Hook = hook, Act = act });
	}

	// Disconnected by the game or Lua (Hooks:Disconnect, a dishook): forgotten, so the list does not grow.
	internal static void Forget(LuaHookBase hook, Closure act)
	{
		Script script = act?.OwnerScript;
		if (script == null || !Connections.TryGetValue(script, out List<Connection> list))
		{
			return;
		}
		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (ReferenceEquals(list[i].Hook, hook) && ReferenceEquals(list[i].Act, act))
			{
				list.RemoveAt(i);
			}
		}
	}

	private static void OnScriptKilled(Script script)
	{
		script.OnScriptKilled -= OnScriptKilled;
		if (!Connections.TryGetValue(script, out List<Connection> list))
		{
			return;
		}
		Connections.Remove(script);
		foreach (Connection connection in list)
		{
			LuaHookBase.DisconnectFromCLR(connection.Hook, connection.Act);
		}
	}
}

[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(LuaHookBase), nameof(LuaHookBase.DisconnectFromCLR))]
internal static class LuaHookDisconnectPatch
{
	private static void Postfix(LuaHookBase hook, Closure act)
	{
		LuaHookKillPatch.Forget(hook, act);
	}
}
