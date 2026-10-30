using System.Collections.Generic;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Every frame each unit's Locomotion4 applies its animator's motion: it moves a helper transform to where the unit
// should be, levels its facing, looks up the floor height under it (route graph and surface lookups) and sets the
// unit's rotation and position. A unit standing still (most of them, most of the time) ends where it started. When the
// last full pass changed nothing, the floor was exactly at the unit's feet and the unit has not moved since, the pass
// is skipped for up to RecheckSeconds; then it runs in full again, so a floor that changes under a still unit is seen
// within a quarter second (falling is handled apart, by the grounded check every frame). Only while no target match
// is running (the animator's motion then moves the unit) and while the unit snaps to the floor.
// The pass itself, when it runs, is the lean one of LocomotionSavers where it can be, else the game's own.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(Locomotion4), "ApplyAnimatorMotion")]
internal static class IdleUnitMotion
{
	private const float RecheckSeconds = 0.25f;

	internal struct Still
	{
		internal float Until;

		internal Vector3 Position;

		internal Quaternion Rotation;
	}

	internal static readonly Dictionary<Locomotion4, Still> StillUnits = new Dictionary<Locomotion4, Still>();

	private struct Before
	{
		internal bool Track;

		internal Vector3 Position;

		internal Quaternion Rotation;
	}

	private static bool Prefix(Locomotion4 __instance, out Before __state)
	{
		__state = default;
		if (!LocomotionSavers.Ready(__instance))
		{
			StillUnits.Remove(__instance);
			return true;
		}
		if (!__instance.AutoSnapToFloor || __instance.targetMatcher.State != TargetMatcher.MatchState.Sleeping)
		{
			StillUnits.Remove(__instance);
			return !LocomotionSavers.ApplyMotion(__instance);
		}
		LocomotionSavers.Read(__instance.trf, out Vector3 position, out Quaternion rotation);
		if (StillUnits.TryGetValue(__instance, out Still still) && Time.time < still.Until && still.Position == position && still.Rotation == rotation)
		{
			return false;
		}
		// With no target match running the target matcher hands over where the unit is (ConsumePosition and
		// ConsumeOrientation give the transform's own), so what was just read is the motion.
		if (LocomotionSavers.Apply(__instance, position, rotation, position, rotation, out Vector3 endPosition, out Quaternion endRotation))
		{
			Remember(__instance, position, rotation, endPosition, endRotation);
			return false;
		}
		__state = new Before { Track = true, Position = position, Rotation = rotation };
		return true;
	}

	private static void Postfix(Locomotion4 __instance, Before __state)
	{
		if (!__state.Track)
		{
			return;
		}
		LocomotionSavers.Read(__instance.trf, out Vector3 position, out Quaternion rotation);
		Remember(__instance, __state.Position, __state.Rotation, position, rotation);
	}

	// A pass that left the unit where it was, on a floor exactly at its feet, makes it still for a moment.
	private static void Remember(Locomotion4 locomotion, Vector3 before, Quaternion beforeRotation, Vector3 position, Quaternion rotation)
	{
		if (position == before && rotation == beforeRotation && locomotion.GetFloorHeight(position, out float floor) && Mathf.Abs(floor - position.y) < 0.0001f)
		{
			StillUnits[locomotion] = new Still { Until = Time.time + RecheckSeconds, Position = position, Rotation = rotation };
		}
		else
		{
			StillUnits.Remove(locomotion);
		}
	}
}
