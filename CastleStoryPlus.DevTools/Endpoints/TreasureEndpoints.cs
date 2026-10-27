using CastleStoryPlus.Core;
using System;
using System.Collections.Generic;
using Brix.Game;
using Brix.Game.AI;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.Treasure;
using UnityEngine;
using UnityEngine.Networking;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Treasure chests and their beasts: the chests of this world, and spawning a beast to look at.
internal static class TreasureEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/treasure", "The treasure chests of this world: position, guards (2-10, the size of its loot), opened, guarded (a living beast within the guard radius)", Chests));
		routes.Add(new Route("POST", "/api/treasure/beast", "Spawns a beast (host): Spirestalker, Cindermane, Boulderback or Ironhorn, of the Corruptrons faction, without a raid (it fights what comes near)", Beast)
		{
			Parameters = "kind (Spirestalker, Cindermane, Boulderback, Ironhorn; required), at (x,y,z; required), level (1-10, default 1)"
		});
	}

	private static object Chests(ApiRequest request)
	{
		List<object> chests = new List<object>();
		foreach (object[] chest in TreasureChests.Describe())
		{
			chests.Add(Obj("at", Vec((Vector3)chest[0]), "guards", chest[1], "opened", chest[2], "guarded", chest[3]));
		}
		return Obj("count", chests.Count, "chests", chests);
	}

	private static object Beast(ApiRequest request)
	{
		if (!NetworkServer.active)
		{
			throw new ApiException(409, "Only on the host");
		}
		BeastKind kind = (BeastKind)Enum.Parse(typeof(BeastKind), request.Required("kind"), true);
		Faction enemy = Faction.FindByName("Corruptrons") ?? Faction.DefaultEnemy;
		if (enemy == null)
		{
			throw new ApiException(409, "No enemy faction in this game");
		}
		Labor labor = Beasts.Spawn(kind, enemy, request.Vector("at"), request.Int("level", 1));
		if (labor == null)
		{
			throw new ApiException(500, "The game could not spawn the unit");
		}
		return Obj("kind", kind.ToString(), "id", labor.gameObject.InstanceId(), "at", Vec(labor.transform.position));
	}
}
