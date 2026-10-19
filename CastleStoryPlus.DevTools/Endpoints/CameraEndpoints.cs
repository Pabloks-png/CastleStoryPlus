using System.Collections.Generic;
using Brix.Input;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Pointing the game's camera at a place, to look at it in a screenshot.
internal static class CameraEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/camera", "Points the camera at a place, as the player's camera looks: from a distance, a pitch down and a turn round the vertical. Answers where it stands", Look)
		{
			Parameters = "at (x,y,z: what to look at, required), distance (default 14), pitch (degrees down, default 50), yaw (degrees round the vertical, default 45)"
		});
	}

	private static object Look(ApiRequest request)
	{
		CameraScript camera = CameraScript.Instance;
		if (camera == null)
		{
			throw new ApiException(409, "No game camera (start a game first)");
		}
		Vector3 at = request.Vector("at");
		float distance = Mathf.Clamp(request.Float("distance", 14f), 2f, 300f);
		Quaternion rotation = Quaternion.Euler(Mathf.Clamp(request.Float("pitch", 50f), 5f, 89f), request.Float("yaw", 45f), 0f);
		Vector3 position = at - rotation * Vector3.forward * distance;
		camera.Positionner(position, rotation, distance);
		return Obj("position", position, "target", at, "distance", distance);
	}
}
