using Brix.Engine;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Every frame each unit's Locomotion4 applies its animator's motion (ApplyAnimatorMotion) by way of a helper
// transform: it writes where the unit should go into a hidden child object (MoveTo), reads it back to level its
// facing, writes the facing, reads the position four times to look up and set the floor height, writes it, then reads
// both back again to set them on the unit; on the unit it writes the rotation every time, also when it is the same.
// Some sixteen transform calls per unit and frame, each one a call into the engine, for what is a few lines of
// arithmetic. The same work is now done on plain values: the motion the target matcher hands over, the facing levelled
// the same way, the same floor lookup and the same snap check, and only then is the unit's transform touched: its
// rotation when it differs, its position (through its voxel transform, as before) when it moved. A walking unit that
// faces its goal no longer has its whole bone hierarchy marked as changed for a rotation that did not change.
// Only the plain case is done here: a unit that snaps to the floor, stands on ground, and whose floor is where it
// may step. Everything else (climbing, stepping down, ladders, jumping, flailing, a floor it may not snap to, a move
// of more than one block) is left to the game's own pass, which then runs from the start; nothing is changed before
// that is known, and what this reads (the target matcher's motion, the ground state, the floor height) the game reads
// again unchanged. Target matching is not touched: its motion is taken from the target matcher as before.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
internal static class LocomotionSavers
{
	// Whether the lean pass can stand in for the game's on this unit at all.
	internal static bool Ready(Locomotion4 locomotion)
	{
		return locomotion.trf != null && locomotion.targetMatcher != null && locomotion.VoxelTransform != null && locomotion.States != null && locomotion.Common != null && locomotion.Common.Grounded != null;
	}

	// Applies the motion from the target matcher. False when the game's own pass has to run instead (nothing was
	// changed then).
	internal static bool ApplyMotion(Locomotion4 locomotion)
	{
		Vector3 position = locomotion.targetMatcher.ConsumePosition();
		Quaternion rotation = locomotion.targetMatcher.ConsumeOrientation();
		Read(locomotion.trf, out Vector3 current, out Quaternion currentRotation);
		return Apply(locomotion, position, rotation, current, currentRotation, out _, out _);
	}

	// The game's ComputeAnimatorMotion, SnapToFloor and SetPosition on plain values, from where the motion goes
	// (position, rotation) and where the unit is (current, currentRotation). Gives where the unit ends. False, with
	// nothing changed, when the case is not the plain one.
	internal static bool Apply(Locomotion4 locomotion, Vector3 position, Quaternion rotation, Vector3 current, Quaternion currentRotation, out Vector3 endPosition, out Quaternion endRotation)
	{
		endPosition = current;
		endRotation = currentRotation;
		if (!locomotion.AutoSnapToFloor || !locomotion.Common.Grounded.IsGrounded())
		{
			return false;
		}
		// The facing levelled as SnapToFloor does it.
		Vector3 forward = rotation * Vector3.forward;
		rotation = Quaternion.LookRotation(forward - Vector3.Project(forward, Vector3.up), Vector3.up);
		if (!locomotion.GetFloorHeight(position, out float floor) || !locomotion.IsSnappable(position.y, floor))
		{
			return false;
		}
		position = new Vector3(position.x, floor, position.z);
		// SetPosition: no move when the unit is there already (the game's own comparison) or the goal is the origin,
		// and a move of more than one block at once is refused by the game (with a log line) before anything else.
		bool move = !(current == position) && !(position == Vector3.zero);
		if (move)
		{
			XYZ from = XYZ.FromVector3(current);
			XYZ to = XYZ.FromVector3(position);
			if (from != to && XYZ.MaxMagnitude(from - to) > 1)
			{
				return false;
			}
		}
		if (!SameBits(rotation, currentRotation))
		{
			locomotion.trf.rotation = rotation;
			endRotation = rotation;
		}
		if (move)
		{
			locomotion.VoxelTransform.MoveTransform(position);
			endPosition = position;
		}
		return true;
	}

	internal static void Read(Transform transform, out Vector3 position, out Quaternion rotation)
	{
#if UNITY6
		transform.GetPositionAndRotation(out position, out rotation);
#else
		position = transform.position;
		rotation = transform.rotation;
#endif
	}

	// Exactly the same rotation (Unity's == lets small turns pass as equal, which would lose a slow turn).
	private static bool SameBits(Quaternion a, Quaternion b)
	{
		return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
	}
}
