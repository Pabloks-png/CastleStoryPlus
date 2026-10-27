using System.Collections.Generic;
using Brix.Engine;
using Brix.Lua;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.CoreLib;
using UnityEngine;

namespace CastleStoryPlus.WorldGen;

// Single player in a new random world: the player starts at the starting point chosen on the new game screen
// ([RandomWorld] Start). The gamemodes pick it from Markers.GetGroup("Player Starting Point") in their own ways:
// sandbox and conquest take math.random(#points) of the list, invasion its first entry. So the chosen point is put
// first in the list, and the next math.random(#points) in the same frame returns 1. Other starting points are left
// to the gamemode (conquest's corrupted crystals, neutral home crystals).
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch]
internal static class RandomWorldStart
{
	// How far (in blocks, across) a marker may be from the chosen point's position.
	private const float Near = 3f;

	private static int _armedCount;

	private static int _armedFrame = -1;

	[HarmonyPatch(typeof(LuaMarker), nameof(LuaMarker.GetGroup), typeof(string))]
	[HarmonyPostfix]
	private static void ChosenFirst(string group, List<LuaMarker> __result)
	{
		if (group != WorldGenerator.StartGroup || __result == null || __result.Count < 2 || Neo.IsMultiplayer || RandomWorld.ChosenStart == 0 || !RandomWorld.IsRandomWorld(GameParam.Map))
		{
			return;
		}
		List<Vector3> starts = RandomWorld.Starts();
		if (RandomWorld.ChosenStart > starts.Count)
		{
			return;
		}
		Vector3 chosen = starts[RandomWorld.ChosenStart - 1];
		int best = -1;
		float bestDistance = Near;
		for (int i = 0; i < __result.Count; i++)
		{
			Vector3 at = __result[i].position;
			float distance = new Vector2(at.x - chosen.x, at.z - chosen.z).magnitude;
			if (distance <= bestDistance)
			{
				best = i;
				bestDistance = distance;
			}
		}
		if (best < 0)
		{
			Plugin.Log.LogWarning("RandomWorld: no starting point at the chosen one " + chosen);
			return;
		}
		LuaMarker marker = __result[best];
		__result.RemoveAt(best);
		__result.Insert(0, marker);
		_armedCount = __result.Count;
		_armedFrame = Time.frameCount;
	}

	[HarmonyPatch(typeof(MathModule), nameof(MathModule.random))]
	[HarmonyPrefix]
	private static bool PickChosen(CallbackArguments args, ref DynValue __result)
	{
		if (_armedFrame != Time.frameCount || args.Count != 1)
		{
			return true;
		}
		DynValue count = args[0];
		if (count.Type != DataType.Number || (int)count.Number != _armedCount)
		{
			return true;
		}
		_armedFrame = -1;
		__result = DynValue.NewNumber(1);
		Plugin.Log.LogInfo("RandomWorld: starting at the chosen starting point " + RandomWorld.ChosenStart);
		return false;
	}
}
