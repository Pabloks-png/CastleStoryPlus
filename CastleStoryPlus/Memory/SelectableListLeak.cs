using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.UI.GameSelector;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// Every selectable thing (worker, block, building, project, siege engine...) registers in UIGameSelector's list when
// taken from its pool, for the selection box. UIGameSelector.Unregister had its test inverted (it removed the object
// only when it was NOT in the list), so nothing was ever removed: the list kept every selectable of the session,
// destroyed ones included with all their fields, and each new registration searched all of it. The heap scan put
// some 66 MB behind UIGameSelector. Unregister now removes the object (after the same unhover and unselect as before).
// The selection box already skipped released and destroyed entries, so it selects the same things.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(UIGameSelector), nameof(UIGameSelector.Unregister))]
internal static class SelectableUnregisterPatch
{
	private static bool Prefix(UISelectable selectable)
	{
		UIGameSelector.Unhover(selectable);
		UIGameSelector.Unselect(selectable);
		List<UISelectable> list = UIGameSelector.instance.RegisteredList;
		// Searched from the end: things released soon after being taken (projectiles, litter, blocks moved around)
		// are the newest entries.
		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (ReferenceEquals(list[i], selectable))
			{
				list.RemoveAt(i);
				break;
			}
		}
		return false;
	}
}

// Selectables destroyed instead of released to their pool never unregister. The list drops destroyed entries each
// time it has doubled since the last check, so such leftovers cannot pile up during a game.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(UIGameSelector), nameof(UIGameSelector.Register))]
internal static class SelectableRegisterPrune
{
	private const int MinCheck = 1024;

	private static readonly Predicate<UISelectable> IsDestroyed = (UISelectable s) => s == null;

	private static int _checkAt = MinCheck;

	private static void Postfix()
	{
		UIGameSelector selector = UIGameSelector.instance;
		if (ReferenceEquals(selector, null))
		{
			return;
		}
		List<UISelectable> list = selector.RegisteredList;
		// A new, smaller list (after leaving a game) lowers the threshold again.
		if (list.Count * 4 < _checkAt)
		{
			_checkAt = Math.Max(MinCheck, list.Count * 2);
		}
		if (list.Count < _checkAt)
		{
			return;
		}
		list.RemoveAll(IsDestroyed);
		_checkAt = Math.Max(MinCheck, list.Count * 2);
	}
}

// Leaving a game emptied nothing: the static UIGameSelector.instance kept the whole game's selectables in the menu and
// into the next game. Nothing selects between leaving and the next game; objects still released later find nothing to
// remove, and pooled ones register again when they are taken in the next game.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(Architecte), "LeaveGame")]
internal static class SelectableLeaveGame
{
	private static void Postfix()
	{
		UIGameSelector selector = UIGameSelector.instance;
		if (!ReferenceEquals(selector, null))
		{
			selector.RegisteredList.Clear();
			selector.RegisteredList.TrimExcess();
		}
	}
}
