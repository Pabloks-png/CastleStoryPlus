using UnityEngine;

namespace CastleStoryPlus.Diagnostics;

// Where spawned loose items go: many dropped in one point overlap, and the physics engine then pushes them apart so
// hard that they fly off in all directions. Each gets its own place in a square of up to 8 x 8 on the ground, with
// further layers of it stacked over, and while it settles the physics may push it out of another at a walking pace
// only (its maximum depenetration velocity), so a heap that still touches only slides apart.
internal static class SpawnSpread
{
	// The room per item: about a block, a little more so a log or a crate does not touch the next.
	private const float Spacing = 1.25f;

	private const int MaxSide = 8;

	// How fast (m/s) the physics may push an item out of another; Unity's default is 10.
	private const float MaxDepenetration = 1f;

	// The place of item index of count around center (center is on the ground).
	internal static Vector3 Place(Vector3 center, int index, int count)
	{
		int side = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count)), 1, MaxSide);
		int perLayer = side * side;
		int layer = index / perLayer;
		int inLayer = index % perLayer;
		float offset = (side - 1) * 0.5f;
		return center + new Vector3((inLayer % side - offset) * Spacing, 0.5f + layer * Spacing, (inLayer / side - offset) * Spacing);
	}

	// Lets the physics push the item out of others only slowly.
	internal static void Calm(GameObject item)
	{
		if (item == null)
		{
			return;
		}
		foreach (Rigidbody body in item.GetComponentsInChildren<Rigidbody>())
		{
			body.maxDepenetrationVelocity = MaxDepenetration;
		}
	}
}
