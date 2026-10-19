using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.Game.Components;
using Brix.Game.Components.Validators.Graphs;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// A compound blueprint (a paste with Ctrl+V, a workshop blueprint, the game's copy and move of blueprints) builds a
// support graph of its pieces: each piece's node gets the pieces holding up its support cells as parents, made first
// (BlueprintValidator.GenerateBlueprintValidationNode). A node is put in the graph only after its parents are made, so
// two pieces that hold each other up made the game recurse without end and crash (a stack overflow, a segfault in
// Mono). The game's own blocks only rest on what is below them, but the mod's long bricks also rest on the block
// beyond their ends (and slabs on the blocks beside them): two long bricks end to end in a wall hold each other up.
// While a node is being made, a piece already being made further up is no longer taken as one of its supports, so the
// graph has no loops; such a piece then checks its support itself, as a piece without parents in the graph does.
[Feature]
[HarmonyPatch(typeof(BlueprintValidator), nameof(BlueprintValidator.GenerateBlueprintValidationNode))]
internal static class BlueprintGraphCycleGuard
{
	// The pieces whose node is being made, from the outermost call in.
	internal static readonly HashSet<GameObject> InProgress = new HashSet<GameObject>();

	private static void Prefix(ref ValidationData vData, out GameObject __state)
	{
		__state = null;
		GameObject go = vData.Volume != null ? vData.Volume.GameObject : null;
		if (go != null && InProgress.Add(go))
		{
			__state = go;
		}
	}

	private static Exception Finalizer(Exception __exception, GameObject __state)
	{
		if ((object)__state != null)
		{
			InProgress.Remove(__state);
		}
		return __exception;
	}
}

// The supports of a support cell among the compound's pieces; used only while making a blueprint graph node.
[Feature]
[HarmonyPatch(typeof(VUtil), nameof(VUtil.GetSupports), typeof(XYZ), typeof(VPropertyMask), typeof(List<ValidationData>))]
internal static class BlueprintGraphSupportFilter
{
	private static void Postfix(List<ValidationData> __result)
	{
		if (__result == null || BlueprintGraphCycleGuard.InProgress.Count == 0)
		{
			return;
		}
		for (int i = __result.Count - 1; i >= 0; i--)
		{
			GameObject target = __result[i].Target;
			if ((object)target != null && BlueprintGraphCycleGuard.InProgress.Contains(target))
			{
				__result.RemoveAt(i);
			}
		}
	}
}
