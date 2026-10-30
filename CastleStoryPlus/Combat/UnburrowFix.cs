using System.Collections;
using Brix.Game.AI;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Combat;

// A unit that digs itself out of the ground when it spawns (UnburrowOnSpawn: corruptrons, the treasure beasts) asks its
// Locomotion4 to unburrow from Start, but Unity does not order the Starts of one object's components, and when
// UnburrowOnSpawn's comes first the locomotion has no controller yet: DoUnburrow threw (NullReferenceException) and
// the unit stayed in its spawn pose. The unburrow now waits until the locomotion is ready (a few frames at most).
[Feature]
[HarmonyPatch(typeof(UnburrowOnSpawn), "Unburrow")]
internal static class UnburrowFix
{
	// Frames to wait for the locomotion before giving up.
	private const int MaxFrames = 120;

	private static bool Prefix(UnburrowOnSpawn __instance)
	{
		Locomotion4 locomotion = __instance.GetComponent<Locomotion4>();
		if (locomotion == null || locomotion.controller != null)
		{
			return true;
		}
		__instance.StartCoroutine(UnburrowWhenReady(locomotion));
		return false;
	}

	private static IEnumerator UnburrowWhenReady(Locomotion4 locomotion)
	{
		for (int frame = 0; frame < MaxFrames; frame++)
		{
			yield return null;
			if (locomotion == null)
			{
				yield break;
			}
			if (locomotion.controller != null)
			{
				locomotion.DoUnburrow();
				yield break;
			}
		}
		Plugin.Log.LogWarning("Unburrow: " + locomotion.name + " has no locomotion controller after " + MaxFrames + " frames; it does not unburrow");
	}
}
