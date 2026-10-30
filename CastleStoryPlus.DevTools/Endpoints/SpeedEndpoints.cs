using System.Collections.Generic;
using System.Globalization;
using Brix.Engine;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.DevTools.Speed;
using CastleStoryPlus.Diagnostics;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Game speed for testing: read it with the speed actually reached, and fast-forward up to 20x (host/single player).
internal static class SpeedEndpoints
{
	// How long the measured speed looks back, in real seconds.
	private const float MeasureSeconds = 3f;

	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/speed", "Game speed: the chosen speed, Time.timeScale, paused, fixed timestep, maximumDeltaTime, and the speed actually reached (game seconds per real second over the last seconds) with frames per second and fixed steps per frame", Get)
		{
			Parameters = "seconds (window of the measured speed, default 3)"
		});
		routes.Add(new Route("POST", "/api/pause", "Pauses or unpauses the game as the P key does (single player or host). Answers as GET /api/speed", Pause)
		{
			Parameters = "paused (true or false, required)"
		});
		routes.Add(new Route("POST", "/api/speed", "Sets the game speed for testing (single player or host of a game without other players only); 1 goes back to normal. Back to 1x on its own when forSeconds runs out, when a map loads or the player leaves to the menu", Set)
		{
			Parameters = "speed (1, 2, 3, 5, 10, 15, 20 or any from 0.25 to 20, required), forSeconds (real seconds until it goes back to 1x by itself; 0 or missing: until changed)"
		});
	}

	private static object Get(ApiRequest request)
	{
		return State(request.Float("seconds", MeasureSeconds));
	}

	// The game's own pause (the P key), single player or host.
	private static object Pause(ApiRequest request)
	{
		string wanted = request.Required("paused").Trim().ToLowerInvariant();
		if (wanted != "true" && wanted != "false")
		{
			throw new ApiException(400, "'paused' must be true or false, was " + wanted);
		}
		if ((wanted == "true") != ClockConfig.Paused)
		{
			Brix.Input.InputModeController.TogglePause();
		}
		return Get(request);
	}

	private static object Set(ApiRequest request)
	{
		string text = request.Required("speed").Trim().TrimEnd('x', 'X');
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
		{
			throw new ApiException(400, "'speed' is not a number: " + text);
		}
		if (speed < GameSpeed.MinSpeed || speed > GameSpeed.MaxSpeed)
		{
			throw new ApiException(400, "'speed' must be from " + GameSpeed.MinSpeed + " to " + GameSpeed.MaxSpeed + ", was " + text);
		}
		float forSeconds = request.Float("forSeconds", 0f);
		if (forSeconds < 0f)
		{
			throw new ApiException(400, "'forSeconds' must not be negative");
		}
		if (Neo.multiplayer)
		{
			throw new ApiException(409, "The game speed can only change in single player: the game keeps a multiplayer game at normal speed for every player");
		}
		if (!GameSpeed.InGame())
		{
			throw new ApiException(409, "No game in progress (the speed goes back to 1x when a map loads anyway)");
		}
		GameSpeed.Instance.Set(speed, forSeconds);
		return State(MeasureSeconds);
	}

	// The speed the game runs at when not paused.
	private static float Unpaused()
	{
		if (!ClockConfig.Paused)
		{
			return Time.timeScale;
		}
		float value = ClockConfig.GetClockSpeedTimeScale(ClockConfig._unpausedClockSpeed);
		return (value > 0f) ? value : 1f;
	}

	private static object State(float seconds)
	{
		GameSpeed speed = GameSpeed.Instance;
		bool measured = speed.Measure(seconds, out float achieved, out float window, out float fps, out float fixedPerFrame, out float cappedShare);
		return Obj(
			"speed", speed.Active ? speed.Chosen : Unpaused(),
			"setByDevTools", speed.Active,
			"timeScale", Time.timeScale,
			"paused", ClockConfig.Paused,
			"resetInSeconds", (speed.Active && speed.ResetAt > 0f) ? Round(speed.ResetAt - Time.realtimeSinceStartup) : (float?)null,
			"lastReset", speed.LastReset,
			"fixedDeltaTime", Time.fixedDeltaTime,
			"maximumDeltaTime", Round(Time.maximumDeltaTime),
			"measured", measured ? Obj(
				"seconds", Round(window),
				"speed", Round(achieved),
				"fps", Round(fps),
				"fixedStepsPerFrame", Round(fixedPerFrame),
				"framesAtMaxDelta", Round(cappedShare)) : null,
			"quietSpikeWarnings", FrameMonitor.QuietSpikes,
			"multiplayer", Neo.multiplayer,
			"inGame", GameSpeed.InGame());
	}
}
