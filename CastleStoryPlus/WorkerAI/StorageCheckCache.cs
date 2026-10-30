using System.Collections.Generic;
using Brix.External.Factories;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// Whether a worker could take a resource and store it (StorableGoal.CanTakeAndStore: room in its bag and a storage
// with room) was worked out again for every block of a quarry or dig area it weighed, though the answer is the same for
// every block of that resource. With the storage full every block failed that check, and a search through a large
// quarry spent several milliseconds learning the same thing hundreds of times. The answer is now kept for the rest of
// the frame, per worker and resource.
[Feature(Features.FasterTaskSearch, Features.FasterTaskSearchInfo)]
[HarmonyPatch(typeof(StorableGoal), "CanTakeAndStore")]
internal static class StorageCheckCache
{
	private static readonly Dictionary<Factory.AssetKey, bool> Answers = new Dictionary<Factory.AssetKey, bool>();

	private static Labor _labor;

	private static int _frame = -1;

	private static bool Prefix(Labor labor, Factory.AssetKey assetKey, ref bool __result)
	{
		if (labor != _labor || Time.frameCount != _frame)
		{
			Answers.Clear();
			_labor = labor;
			_frame = Time.frameCount;
		}
		if (assetKey != null && Answers.TryGetValue(assetKey, out bool answer))
		{
			__result = answer;
			return false;
		}
		return true;
	}

	private static void Postfix(Labor labor, Factory.AssetKey assetKey, bool __result)
	{
		if (assetKey != null && labor == _labor && Time.frameCount == _frame)
		{
			Answers[assetKey] = __result;
		}
	}
}
