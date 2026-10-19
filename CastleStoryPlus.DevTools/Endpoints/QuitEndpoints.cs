using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Closes the game the way its own Quit does, for tools/start-game.sh --restart: a signal to the process (SIGTERM)
// often left the Linux player hanging, its window frozen, instead of closing.
internal static class QuitEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/quit", "Closes the game (Application.Quit) half a second after answering, without saving", Quit));
	}

	private static object Quit(ApiRequest request)
	{
		GameObject go = new GameObject("CSP_DevToolsQuit");
		Object.DontDestroyOnLoad(go);
		go.AddComponent<QuitSoon>();
		DevToolsPlugin.Log.LogInfo("DevTools: quitting the game (POST /api/quit)");
		return Obj("quitting", true);
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
				Application.Quit();
			}
		}
	}
}
