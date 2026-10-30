using BepInEx.Configuration;
using Brix.Engine;
using Brix.NewUI.UserSettings_;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Outside a running game (main menu, lobby, while a world loads) the frame rate is held at most at [Performance]
// MenuFpsCap. The menus draw next to nothing, so without vsync and with the game's own limit off they ran at thousands
// of frames a second (5000 for a moment while a world loads), which only heats the graphics card and makes it whine.
// In a game the player's own limit (Options) applies as before: the limit is put back the moment the game runs.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
internal sealed class MenuFrameCap : MonoBehaviour
{
	private static ConfigEntry<int> _cap;

	private static MenuFrameCap _instance;

	// The limit the game had set (from the player's options) while the cap holds, to put back in a game.
	private int _gameRate;

	private bool _capping;

	// The limit this cap set last.
	private int _lastSet = -2;

	private static void Enable()
	{
		_cap = Plugin.Cfg.Bind("Performance", "MenuFpsCap", 300, new ConfigDescription("Most frames a second outside a running game (menus, loading). 0: no cap.", new AcceptableValueRange<int>(0, 1000)));
		_instance = Plugin.Root.AddComponent<MenuFrameCap>();
	}

	// The player's own limit when the cap holds it down, else the limit as it is.
	internal static int PlayerRate(int rate)
	{
		return (_instance != null && _instance._capping && rate == _instance._lastSet) ? _instance._gameRate : rate;
	}

	private void LateUpdate()
	{
		int cap = _cap.Value;
		bool playing = Architecte.commence && !Architecte.termine;
		if (playing || cap <= 0)
		{
			if (_capping)
			{
				_capping = false;
				// Unless the game set its own limit meanwhile (the options applied), the player's goes back.
				if (Application.targetFrameRate == _lastSet)
				{
					Application.targetFrameRate = _gameRate;
				}
			}
			return;
		}
		int rate = Application.targetFrameRate;
		if (_capping && rate == _lastSet)
		{
			return;
		}
		// A limit the game set (at start, or the options applied in the menu): kept to put back, capped here.
		_gameRate = rate;
		_capping = true;
		_lastSet = (rate > 0 && rate <= cap) ? rate : cap;
		Application.targetFrameRate = _lastSet;
	}
}

// The options read the limit back from Unity when they are saved: the player's own, not the menu's cap.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(UserSettingManager), nameof(UserSettingManager.GetSettingsFromGame))]
internal static class MenuFrameCapSettingsPatch
{
	private static void Postfix(UserSettings __result)
	{
		if (__result != null && __result.graphic != null)
		{
			__result.graphic.targetFPS = MenuFrameCap.PlayerRate(__result.graphic.targetFPS);
		}
	}
}
