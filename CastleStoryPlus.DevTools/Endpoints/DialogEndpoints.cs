using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.LuaUi;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The dialog in front of the game (CsModal): a gamemode's intro ("I'M READY"), victory and defeat dialogs, the mod's
// own dialogs. Reading it and pressing one of its buttons as a click does, so a world started through the API can be
// played without a click on the intro.
internal static class DialogEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/dialog", "The dialog open in front of the game (the gamemode's intro, victory or defeat dialog, the mod's dialogs): title, subtitle and buttons, or open false", Get));
		routes.Add(new Route("POST", "/api/dialog/press", "Presses a button of the open dialog as a click does, e.g. the intro's \"I'M READY\" that starts the game", Press)
		{
			Parameters = "button (index from GET /api/dialog; default: the button Enter presses, the first one)"
		});
	}

	private static object Get(ApiRequest request)
	{
		return State();
	}

	private static object Press(ApiRequest request)
	{
		ModalSpec spec = CsModal.CurrentSpec;
		if (spec == null)
		{
			throw new ApiException(409, "No dialog open (a dialog of the game's own Lua menus, with [LuaUi] CsModals off or an in-game menu that did not build, can not be pressed)");
		}
		int index = request.Int("button", (spec.DefaultButton >= 0) ? spec.DefaultButton : 0);
		if (index < 0 || index >= spec.Buttons.Count)
		{
			throw new ApiException(400, "'button' must be from 0 to " + (spec.Buttons.Count - 1) + ", was " + index);
		}
		string pressed = spec.Buttons[index].Title;
		if (!CsModal.Press(index))
		{
			throw new ApiException(409, "The dialog closed before its button could be pressed");
		}
		Dictionary<string, object> result = State();
		result["pressed"] = pressed;
		return result;
	}

	private static Dictionary<string, object> State()
	{
		ModalSpec spec = CsModal.CurrentSpec;
		if (spec == null)
		{
			return Obj("open", false);
		}
		List<object> buttons = new List<object>();
		for (int i = 0; i < spec.Buttons.Count; i++)
		{
			ModalButton button = spec.Buttons[i];
			buttons.Add(Obj("index", i, "title", button.Title, "info", button.Info, "default", i == spec.DefaultButton));
		}
		return Obj("open", true, "title", spec.Title, "subtitle", spec.Subtitle, "buttons", buttons);
	}
}
