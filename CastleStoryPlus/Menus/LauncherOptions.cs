using System;
using System.IO;
using System.Text;
using BepInEx;
using CastleStoryPlus.Core;

namespace CastleStoryPlus.Menus;

// The game's launcher (the Electron window with Play) saves the resolution, monitor and "Launch in fullscreen" when
// Play is clicked, but never kept them: the save is asynchronous, and the launcher then busy-waited a second
// (which also holds up the save) and quit, so the save never got written. The launcher's script is fixed on disk
// when the game starts (it takes effect at the next launch): it now quits once the options are saved, at least
// 2 s after starting the game and at most 5 s. Done again after a game update or a file check puts the original back.
[Feature(Features.LauncherOptions, Features.LauncherOptionsInfo)]
internal static class LauncherOptions
{
	private const string AutoPlayMarker = "CastleStoryPlus autoplay";

	private const string AutoPlayAnchor = "  init();\n  loadData();";

	// The developer tools' game_start leaves a one-time flag file; then the launcher presses Play by itself.
	private const string AutoPlay = "  // CastleStoryPlus autoplay: started by the developer tools (game_start), so press Play once by itself.\n  (function () {\n    var fs = require('fs');\n    var flag = __dirname + '/../../../../BepInEx/devtools/autoplay';\n    if (fs.existsSync(flag)) {\n      fs.unlinkSync(flag);\n      setTimeout(function () { $('.play').first().click(); }, 1500);\n    }\n  })();\n\n";

	private const string OldQuit = "    var app = require('electron').remote.app\n    wait(1000);\n    app.quit();";

	// The first version quit as soon as the options were saved, a few ms after starting the game, and the game
	// did not survive that; replaced by NewQuit when found.
	private const string FirstQuit = "    // CastleStoryPlus: quit once the options are saved (the busy wait held up the save, so it was never written).\n    var quit = function () { app.quit(); };\n    if (window.cspOptionsSaved) { quit(); } else { window.cspQuit = quit; setTimeout(quit, 3000); }";

	private const string NewQuitBody = "    // CastleStoryPlus: quit once the options are saved (the busy wait held up the save, so it was never written),\n    // but not before the game has had 2 s to start (it does not survive the launcher quitting at once).\n    var quit = function () { app.quit(); };\n    var started = Date.now();\n    window.cspQuit = function () { setTimeout(quit, Math.max(0, 2000 - (Date.now() - started))); };\n    if (window.cspOptionsSaved) { window.cspQuit(); } else { setTimeout(quit, 5000); }";

	private const string NewQuit = "    var app = require('electron').remote.app\n" + NewQuitBody;

	private const string OldSaved = "    }, function(error) {\n      if (error) throw error;\n    });";

	private const string NewSaved = "    }, function(error) {\n      window.cspOptionsSaved = true;\n      if (window.cspQuit) { window.cspQuit(); }\n      if (error) throw error;\n    });";

	private static void Enable()
	{
		string path = Path.Combine(Paths.GameRootPath, "resources/app/app/js/index.js");
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			string original = File.ReadAllText(path, Encoding.UTF8);
			string newline = original.Contains("\r\n") ? "\r\n" : "\n";
			string text = original.Replace("\r\n", "\n");
			if (text.Contains(FirstQuit))
			{
				text = text.Replace(FirstQuit, NewQuitBody);
				Plugin.Log.LogInfo("LauncherOptions: updated the launcher fix (" + path + ")");
			}
			if (!text.Contains(AutoPlayMarker) && text.Contains(AutoPlayAnchor))
			{
				text = text.Replace(AutoPlayAnchor, AutoPlay + AutoPlayAnchor);
				Plugin.Log.LogInfo("LauncherOptions: the launcher can press Play by itself for the developer tools (" + path + ")");
			}
			if (!text.Contains(NewQuit))
			{
				if (!text.Contains(OldQuit) || !text.Contains(OldSaved))
				{
					Plugin.Log.LogWarning("LauncherOptions: the launcher script is not as expected, left alone: " + path);
					return;
				}
				text = text.Replace(OldQuit, NewQuit).Replace(OldSaved, NewSaved);
				Plugin.Log.LogInfo("LauncherOptions: the launcher now remembers its options (" + path + ")");
			}
			if (text != original.Replace("\r\n", "\n"))
			{
				File.WriteAllText(path, text.Replace("\n", newline), new UTF8Encoding(false));
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("LauncherOptions: could not fix the launcher script: " + ex.Message);
		}
	}
}
