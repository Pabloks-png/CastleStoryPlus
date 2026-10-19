using BepInEx.Configuration;

namespace CastleStoryPlus.Core;

// [Debug] DebugMode: the debug windows (F7 performance, F8 debug menu, F9 system log) only open while it is on.
// Switched in "Settings > Castle Story Plus settings" or the game's Options > Gameplay (UI/DebugModeOption.cs) and applies at once; turning it off closes open windows.
// Measuring and logging go on in the background, and the developer API works regardless.
[Feature]
internal static class DebugMode
{
	private static ConfigEntry<bool> _entry;

	// Bound on first use, so the windows can ask whatever order the features are loaded in.
	internal static ConfigEntry<bool> Entry
	{
		get
		{
			if (_entry == null)
			{
				_entry = Plugin.Cfg.Bind("Debug", "DebugMode", false, "Allow the debug windows: performance (F7), debug menu (F8) and system log (F9). Can be switched in Settings > Castle Story Plus settings or in Options > Gameplay.");
			}
			return _entry;
		}
	}

	internal static bool On => Entry.Value;

	private static void Enable()
	{
		// Bind at startup so the entry is in the config file even before anything asks.
		_ = Entry;
	}
}
