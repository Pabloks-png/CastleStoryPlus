using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Closes the game the way its own Quit does, for tools/start-game.sh --restart: a signal to the process (SIGTERM)
// often left the Linux player hanging, its window frozen, instead of closing. With save=true it saves first, as the
// quit menu's "save and quit" does (the loaded manual save overwritten, else a new save), so a restart keeps the game.
// It goes through the game's ApplicationQuitter.Quit, which the mod's FastQuit turns into: wait for a save still being
// written, then end the process. Unity's Application.Quit, used before, sometimes left the Linux player running.
internal static class QuitEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/quit", "Closes the game (as its own quit does: a save still being written finishes, then the process ends) half a second after answering; with save=true it first saves the game as the quit menu's save and quit does", Quit)
		{
			Parameters = "save (true: save the running game first; nothing closes when the save fails)"
		});
	}

	private static object Quit(ApiRequest request)
	{
		bool save = request.Bool("save", false);
		if (save && Brix.Engine.Architecte.instance != null && GameParam.Map != null)
		{
			if (!CastleStoryPlus.Menus.QuitDialogPatch.SaveBeforeLeaving())
			{
				throw new ApiException(500, "The game could not be saved; it is left running (see /api/log?problems=true)");
			}
		}
		GameObject go = new GameObject("CSP_DevToolsQuit");
		Object.DontDestroyOnLoad(go);
		go.AddComponent<QuitSoon>();
		DevToolsPlugin.Log.LogInfo("DevTools: quitting the game (POST /api/quit" + (save ? ", saved first" : string.Empty) + ")");
		return Obj("quitting", true, "saved", save);
	}

	private sealed class QuitSoon : MonoBehaviour
	{
		private float _at;

		private void Start()
		{
			_at = Time.unscaledTime + 0.5f;
		}

		private void Update()
		{
			if (Time.unscaledTime >= _at)
			{
				enabled = false;
				Brix.Utils.ApplicationQuitter.Quit();
			}
		}
	}
}
