using Brix.Engine;
using Brix.Game;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Pathfinding;

// The four small posts of a built rope bridge (two at each end, beside the first and last plank) are obstacles in the
// voxel they stand in and the one above. Put on a wall walk one block wide, a post closed the walk: the route search
// went round them only if there was a way round, and a bricktron on the wall beyond the bridge could not get off it
// ("Can't reach my destination"). Bricktrons of the bridge's own and allied teams now walk past the posts (the route
// search, the walk and the reachability maps let them through); enemies are still stopped by them.
[Feature(Features.Pathfinding, Features.PathfindingInfo)]
[HarmonyPatch(typeof(Volume), nameof(Volume.Initialize))]
internal static class RopeBridgePosts
{
	private sealed class PostPassThrough : IPassThrough
	{
		private readonly Volume _volume;

		public PostPassThrough(Volume volume)
		{
			_volume = volume;
		}

		public bool IsWalkable()
		{
			return true;
		}

		public bool CanSearchThrough(GameObject character)
		{
			return CanPassThrough(character);
		}

		public bool CanPassThrough(GameObject character)
		{
			if (character == null || _volume == null)
			{
				return false;
			}
			Faction own = _volume.Faction;
			Faction other = Faction.GetFaction(character);
			return own != null && other != null && Affiliation.IsAllied(own, other);
		}

		public int GetNiveauxAt(XYZ voxel)
		{
			return 0;
		}

		public float GetWalkHeight(XYZ voxel, float dx, float dz)
		{
			return 0f;
		}
	}

	private static readonly System.Reflection.MethodInfo SetPassThrough = AccessTools.PropertySetter(typeof(Volume), nameof(Volume.PassThrough));

	private static void Postfix(Volume __instance)
	{
		if (__instance.GetComponent<RopeBridgeConfigurator>() == null || __instance.GetComponent<Blueprint>() != null)
		{
			return;
		}
		if (SetPassThrough != null && (__instance.PassThrough == null || __instance.PassThrough is NullPassThrough))
		{
			SetPassThrough.Invoke(__instance, new object[] { new PostPassThrough(__instance) });
		}
	}
}
