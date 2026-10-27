using BepInEx.Configuration;
using Brix.Engine;
using Brix.Input;
using Brix.Network;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleStoryPlus.Menus;

// Game speed keys: 1 = normal speed, 2 = [GameSpeed] Speed2 (2x), 3 = [GameSpeed] Speed3 (3x), and with the debug
// menu's "Keys 4/5" on, 4 = [GameSpeed] Speed4 (10x) and 5 = [GameSpeed] Speed5 (20x).
// Uses the game's own clock speeds (ClockConfig): Double gets Speed2, the debug-only VeryFast Speed3, Fast Speed4 and
// a clock speed of the mod's own Speed5, so pausing and resuming keep the chosen speed. Unity advances game time by
// at most Time.maximumDeltaTime per frame, so at 10x and 20x it is raised to keep the speed down to 10 frames per
// second (as DevTools' fast-forward does), and put back at the other speeds. Single player only; the game forces normal speed in multiplayer.
// The keys 1-3 are only bound in the world editor, where this does nothing.
[Feature(Features.SpeedKeys, Features.SpeedKeysInfo)]
internal class SpeedKeys : MonoBehaviour
{
	internal static ConfigEntry<float> Speed2;

	internal static ConfigEntry<float> Speed3;

	private static ConfigEntry<KeyCode> _normalKey;

	private static ConfigEntry<KeyCode> _speed2Key;

	private static ConfigEntry<KeyCode> _speed3Key;

	internal static ConfigEntry<float> Speed4;

	internal static ConfigEntry<float> Speed5;

	private static ConfigEntry<KeyCode> _speed4Key;

	private static ConfigEntry<KeyCode> _speed5Key;

	// DevTools' fast-forward is clock speed 100.
	private const ClockConfig.ClockSpeed Speed5Clock = (ClockConfig.ClockSpeed)101;

	private const float MinFps = 10f;

	// Time.maximumDeltaTime before a fast speed raised it, or 0 while it is not raised.
	private static float _savedMaxDelta;

	private static void Enable()
	{
		Speed2 = Plugin.Cfg.Bind("GameSpeed", "Speed2", 2f, "Game speed of the second speed key.");
		Speed3 = Plugin.Cfg.Bind("GameSpeed", "Speed3", 3f, "Game speed of the third speed key.");
		_normalKey = Plugin.Cfg.Bind("GameSpeed", "NormalSpeedKey", KeyCode.Alpha1, "Key for normal game speed.");
		_speed2Key = Plugin.Cfg.Bind("GameSpeed", "Speed2Key", KeyCode.Alpha2, "Key for game speed Speed2.");
		_speed3Key = Plugin.Cfg.Bind("GameSpeed", "Speed3Key", KeyCode.Alpha3, "Key for game speed Speed3.");
		Speed4 = Plugin.Cfg.Bind("GameSpeed", "Speed4", 10f, "Game speed of the fourth speed key (only with the debug menu's fast speed keys on).");
		Speed5 = Plugin.Cfg.Bind("GameSpeed", "Speed5", 20f, "Game speed of the fifth speed key (only with the debug menu's fast speed keys on).");
		_speed4Key = Plugin.Cfg.Bind("GameSpeed", "Speed4Key", KeyCode.Alpha4, "Key for game speed Speed4.");
		_speed5Key = Plugin.Cfg.Bind("GameSpeed", "Speed5Key", KeyCode.Alpha5, "Key for game speed Speed5.");
		Plugin.Root.AddComponent<SpeedKeys>();
	}

	private void Update()
	{
		// The frame step follows the speed: raised again after a pause at 10x or 20x, put back when the speed is 3x
		// or less (another speed picked) or the game is left. Paused, it is left as it is.
		bool fast = ClockConfig._unpausedClockSpeed == ClockConfig.ClockSpeed.Fast || ClockConfig._unpausedClockSpeed == Speed5Clock;
		if (!Architecte.commence || Architecte.termine)
		{
			AllowDelta(1f);
		}
		else if (Time.timeScale > 0f)
		{
			AllowDelta(fast ? Time.timeScale : 1f);
		}
		ClockConfig.ClockSpeed speed;
		if (Input.GetKeyDown(_normalKey.Value))
		{
			speed = ClockConfig.ClockSpeed.Default;
		}
		else if (Input.GetKeyDown(_speed2Key.Value))
		{
			speed = ClockConfig.ClockSpeed.Double;
		}
		else if (Input.GetKeyDown(_speed3Key.Value))
		{
			speed = ClockConfig.ClockSpeed.VeryFast;
		}
		else if (FastKeys() && Input.GetKeyDown(_speed4Key.Value))
		{
			speed = ClockConfig.ClockSpeed.Fast;
		}
		else if (FastKeys() && Input.GetKeyDown(_speed5Key.Value))
		{
			speed = Speed5Clock;
		}
		else
		{
			return;
		}
		if (!CanChangeSpeed())
		{
			return;
		}
		ClockConfig.ClockSpeedMap[ClockConfig.ClockSpeed.Double] = Speed2.Value;
		ClockConfig.ClockSpeedMap[ClockConfig.ClockSpeed.VeryFast] = Speed3.Value;
		ClockConfig.ClockSpeedMap[ClockConfig.ClockSpeed.Fast] = Speed4.Value;
		ClockConfig.ClockSpeedMap[Speed5Clock] = Speed5.Value;
		AllowDelta(ClockConfig.GetClockSpeedTimeScale(speed));
		if (ClockConfig.Paused)
		{
			// Resumes at this speed when the game is unpaused.
			ClockConfig._unpausedClockSpeed = speed;
		}
		else
		{
			ClockConfig._unpausedClockSpeed = speed;
			ClockConfig.SetTimeScale(speed);
		}
		float value = ClockConfig.GetClockSpeedTimeScale(speed);
		GameSignals.Invoke(GameSignals.OnAutoFadeDialog, "Game speed " + value.ToString("0.#") + "x" + (ClockConfig.Paused ? " (paused)" : string.Empty), 1f);
	}

	private static bool FastKeys()
	{
		return Diagnostics.DebugMenu.FastSpeedKeys != null && Diagnostics.DebugMenu.FastSpeedKeys.Value;
	}

	// Lets Unity advance game time far enough per frame for the speed (raised above 3x, put back below).
	private static void AllowDelta(float speed)
	{
		if (speed > 3.01f)
		{
			if (_savedMaxDelta <= 0f)
			{
				_savedMaxDelta = Time.maximumDeltaTime;
			}
			Time.maximumDeltaTime = Mathf.Max(_savedMaxDelta, speed / MinFps);
		}
		else if (_savedMaxDelta > 0f)
		{
			Time.maximumDeltaTime = _savedMaxDelta;
			_savedMaxDelta = 0f;
		}
	}

	// In a single player game (not the world editor), and not while typing in a text field.
	private static bool CanChangeSpeed()
	{
		if (Neo.multiplayer || !Architecte.commence || Architecte.termine || InputModeController.DefaultMode == InputMode.worldEditor)
		{
			return false;
		}
		GameObject focused = (EventSystem.current != null) ? EventSystem.current.currentSelectedGameObject : null;
		return focused == null || focused.GetComponent<InputField>() == null;
	}
}
