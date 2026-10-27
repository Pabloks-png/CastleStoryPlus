using System;
using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.Invasion;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The map's corruptron spawn points as SpawnDistance sees them: how far each is from the players' blocks, whether it
// may be drawn and its chance, and the point drawn last.
internal static class SpawnEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/spawns", "The corruptron spawn points (invasion): position, blocks to the nearest block a player has built, whether it is far enough to be drawn ([SpawnDistance] MinBlocks) and its chance to be drawn now, and the point drawn last", Spawns));
	}

	private static object Spawns(ApiRequest request)
	{
		List<object> points = new List<object>();
		foreach (object[] row in SpawnDistance.Describe())
		{
			float nearest = (float)row[1];
			points.Add(Obj(
				"at", Vec((Vector3)row[0]),
				"nearestBuilt", (nearest >= 99999f) ? null : (object)Mathf.RoundToInt(nearest),
				"clear", row[2],
				"chancePercent", Math.Round((double)row[3] * 100.0, 1)));
		}
		Vector2? last = SpawnDistance.LastPicked;
		return Obj(
			"minBlocks", SpawnDistance.MinBlocks,
			"builtBlocks", SpawnDistance.BuiltBlocks,
			"lastPicked", last.HasValue ? new float[2] { Round(last.Value.x), Round(last.Value.y) } : null,
			"count", points.Count,
			"points", points);
	}
}
