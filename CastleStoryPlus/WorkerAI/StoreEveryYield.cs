using System.Collections.Generic;
using Brix.External.Factories;
using Brix.Game.AI;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.WorkerAI;

// A worker takes a dig, harvest or clean-up goal only when it could store what it picks up (StorableGoal.CouldStore).
// The game accepted the goal when any one of the things it yields had room: a voxel of clay or ore also gives stones,
// so with the stone pallets free and the clay pallets full the worker dug anyway and was left carrying clay it could
// not put anywhere. Then its bag was too full to dig, it walked from storage to storage and dropped the load at last,
// for the next worker to pick up again. Now every thing the goal yields must have room in the worker's bag and a
// storage, else the goal is left alone (the worker thinks "could not find storage", as before).
[Feature(Features.WorkerAI, Features.WorkerAIInfo)]
[HarmonyPatch(typeof(StorableGoal), "CouldStore")]
internal static class StoreEveryYield
{
	private static bool Prefix(StorableGoal __instance, Labor labor, ref bool __result)
	{
		HashSet<Factory.AssetKey> keys = CollectionPool.RequestHashSet<Factory.AssetKey>();
		__instance.UniqueItems(ref keys);
		bool result = true;
		foreach (Factory.AssetKey key in keys)
		{
			if (!__instance.CanTakeAndStore(labor, key))
			{
				result = false;
				break;
			}
		}
		keys.Dispose();
		__result = result;
		return false;
	}
}
