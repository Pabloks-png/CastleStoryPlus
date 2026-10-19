using System.Collections.Generic;
using Brix.External.Factories;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.InstructionType;
using Brix.Game.AI.Nodes;
using Brix.Game.Components;
using Brix.Game.Network;
using Brix.Game.Semantique;
using Brix.UI.Icons;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;
using Motus.Behavior;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// The player's pick up order on a loose item: every selected bricktron was sent for that one item, so the first to
// get there took it and the others came back empty-handed, and a bricktron took only the item clicked although its
// hands had room for more. Now each bricktron takes an item of the clicked kind that no other one is after (the
// clicked one first, then the nearest), and goes on picking up loose items of that kind lying round it (within
// Radius blocks of the clicked item) until its hands are full or none are left.
[Feature(Features.PickUpAround, Features.PickUpAroundInfo)]
internal static class PickUpAround
{
	// How far round the clicked item the bricktrons look for more of its kind.
	private const float Radius = 6f;

	// Items one order picks up at most (a guard; hands are usually full long before).
	private const int MaxItems = 30;

	// A claim expires on its own in case a task is dropped before the item is picked up.
	private const float ClaimSeconds = 30f;

	// Same layers as the game's cleanup zones: loose stones, free blocks and accessories.
	private const int Layers = 1073152;

	private static readonly Collider[] Hits = new Collider[256];

	private static readonly Dictionary<GameObject, KeyValuePair<Labor, float>> Claims = new Dictionary<GameObject, KeyValuePair<Labor, float>>();

	public static readonly LaborInstruction<GameObject> PickUp = new LaborInstruction<GameObject>("PickUpAround", IconKeys.PickUp, (Labor labor, GameObject item) => PickUpNode(labor, item));

	private static void Enable()
	{
		GameSession.OnLeave(() => Claims.Clear());
	}

	// Not inside a stockpile, rack or crystal, not carried or worn by a unit.
	internal static bool IsLoose(GameObject go)
	{
		if (go.IsNullOrReleased() || !go.activeInHierarchy)
		{
			return false;
		}
		Transform parent = go.transform.parent;
		return parent == null || (parent.GetComponentInParent<Recepteur>() == null && parent.GetComponentInParent<Labor>() == null);
	}

	private static Node PickUpNode(Labor labor, GameObject clicked)
	{
		GameComponent component = (clicked != null) ? clicked.GetComponent<GameComponent>() : null;
		if (component == null)
		{
			return Node.FailWork<Exceptions.CannotFindWhatINeedToPickUp>();
		}
		Factory.AssetKey key = component.AssetKey;
		Vector3 center = clicked.transform.position;
		GameObject first = IsClaimedByOther(clicked, labor) ? Nearest(labor, key, center, clicked) : clicked;
		if (first == null)
		{
			first = clicked;
		}
		return Step(labor, first, key, center, 1);
	}

	// Picks up the item, then the next one of the kind round the clicked spot while the hands have room.
	private static Node Step(Labor labor, GameObject item, Factory.AssetKey key, Vector3 center, int count)
	{
		GameComponent component = item.GetComponent<GameComponent>();
		if (component == null)
		{
			return Node.Empty;
		}
		Claim(item, labor);
		return Node.Sequence.Label("Pick up")
			.Do((Labor l) => l.GoPickUp(component, -1), labor)
			.Do((Labor l) => Next(l, key, center, count), labor);
	}

	private static Node Next(Labor labor, Factory.AssetKey key, Vector3 center, int count)
	{
		if (count >= MaxItems)
		{
			return Node.Empty;
		}
		GameObject item = Nearest(labor, key, center, null);
		return (item == null) ? Node.Empty : Step(labor, item, key, center, count + 1);
	}

	// The loose item of the kind nearest the bricktron, round center, that it has room for and no other one is after.
	private static GameObject Nearest(Labor labor, Factory.AssetKey key, Vector3 center, GameObject except)
	{
		GameObject best = null;
		float bestDistance = float.MaxValue;
		int count = Physics.OverlapSphereNonAlloc(center, Radius, Hits, Layers);
		for (int i = 0; i < count; i++)
		{
			GameObject go = SignificantParent.Of(Hits[i].gameObject);
			if (go == null || go == except || !IsLoose(go) || IsClaimedByOther(go, labor))
			{
				continue;
			}
			GameComponent component = go.GetComponent<GameComponent>();
			if (component == null || component.AssetKey != key || !labor.recepteur.HasRoomFor(go))
			{
				continue;
			}
			float distance = (go.transform.position - labor.transform.position).sqrMagnitude;
			if (distance < bestDistance)
			{
				best = go;
				bestDistance = distance;
			}
		}
		System.Array.Clear(Hits, 0, count);
		return best;
	}

	private static void Claim(GameObject item, Labor labor)
	{
		Claims[item] = new KeyValuePair<Labor, float>(labor, Time.time + ClaimSeconds);
	}

	private static bool IsClaimedByOther(GameObject item, Labor labor)
	{
		if (!Claims.TryGetValue(item, out KeyValuePair<Labor, float> claim))
		{
			return false;
		}
		if (claim.Key == null || claim.Key == labor || Time.time > claim.Value || !IsLoose(item))
		{
			Claims.Remove(item);
			return false;
		}
		return true;
	}
}

// Server: the pick up order on a loose item runs PickUpAround; on anything else (a stockpile, a workshop) the game's.
[Feature(Features.PickUpAround, Features.PickUpAroundInfo)]
[HarmonyPatch(typeof(UNetProjectCmd), nameof(UNetProjectCmd.CmdPickup))]
internal static class PickUpAroundPatch
{
	private static bool Prefix(UNetProjectCmd __instance, GameObject laborGO, GameObject objectToPickupFrom)
	{
		if (laborGO == null || objectToPickupFrom == null || !Affiliation.IsOwnedBy(laborGO, __instance.gameObject) || !PickUpAround.IsLoose(objectToPickupFrom))
		{
			return true;
		}
		Labor labor = laborGO.GetComponent<Labor>();
		if (labor == null || objectToPickupFrom.GetComponent<Recepteur>() != null)
		{
			return true;
		}
		PickUpAround.PickUp.Send(labor, objectToPickupFrom, Labor.DecisionSource.User);
		return false;
	}
}
