using CastleStoryPlus.Core;
using CastleStoryPlus.LuaUi;

namespace CastleStoryPlus.GameUi;

// The sandbox tutorial (LTD.lua: its objectives and step tooltips) is gameplay in Lua, which stays Lua for now.
// GameMenu.lua loaded it in a sandbox game and connected it while open; with GameMenu not loaded, the sandbox's
// own menu (its scoreboard, SandboxMenu.lua, loaded in every sandbox game) does that instead.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
internal static class GameTutorial
{
	private const string File = "Gamemodes/sandbox/Menu/SandboxMenu.lua";

	private static void Enable()
	{
		if (!LuaUiConfig.CsGameMenu)
		{
			return;
		}
		LuaInjection.AddPatch(Features.CsMenus, File, "require(\"LUI2.lua\")\n", LuaInjection.Mode.InsertAfter,
			"require(\"LTD.lua\") -- the sandbox tutorial, once loaded by GameMenu.lua (Castle Story Plus)\n");
		LuaInjection.AddPatch(Features.CsMenus, File, "local OnOpen =\n\tfunction()\n", LuaInjection.Mode.InsertAfter,
			"\t\tif ConnectLTD and not _menu.ltdConnected then _menu.ltdConnected = true ConnectLTD() end\n");
		LuaInjection.AddPatch(Features.CsMenus, File, "local OnClose =\n\tfunction()\n", LuaInjection.Mode.InsertAfter,
			"\t\tif DisconnectLTD and _menu.ltdConnected then _menu.ltdConnected = false DisconnectLTD() end\n");
	}
}
