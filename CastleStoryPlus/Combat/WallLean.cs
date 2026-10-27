using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Combat;

// Ranged fighters on the edge of a wall lean out over it: a shot at an enemy below whose flight the wall's own edge
// blocked (an enemy at the foot of the wall, or along it) is let off from [WallLean] Distance blocks (0.8) out over
// the edge instead. Only when the fighter stands at a drop towards the target (the block in front of it at its feet and
// the one below that are empty) and the target is lower. The game's aiming test before every shot tries the leaning
// spot when the normal one is blocked, and the arrow then starts from there. Players' units only; host and clients
// alike (the test and the shot run where the game runs them).
[Feature(Features.WallLean, Features.WallLeanInfo)]
internal static class WallLean
{
	private static ConfigEntry<float> _distance;

	// Set while the aiming test is run again from the leaning spot.
	private static bool _inner;

	private static void Enable()
	{
		_distance = Plugin.Cfg.Bind("WallLean", "Distance", 0.8f, new ConfigDescription("How far (blocks) a ranged fighter on a wall's edge leans out over it to shoot down.", new AcceptableValueRange<float>(0.2f, 1.5f)));
	}

	// How far out over the edge a shot from the unit at the target starts, or zero.
	internal static Vector3 Lean(GameObject unit, Vector3 target)
	{
		if (unit == null)
		{
			return Vector3.zero;
		}
		Faction faction = Faction.GetFaction(unit);
		if (faction == null || faction.isAI)
		{
			return Vector3.zero;
		}
		Vector3 feet = unit.transform.position;
		Vector3 flat = target - feet;
		flat.y = 0f;
		if (flat.magnitude < 0.5f || target.y > feet.y - 1.5f)
		{
			return Vector3.zero;
		}
		Vector3 direction = flat.normalized;
		XYZ front = XYZ.FromVector3(feet + direction * 0.9f + Vector3.up * 0.5f);
		if (!Voxel.IsEmpty(front) || !Voxel.IsEmpty(new XYZ(front.x, front.y - 1, front.z)))
		{
			return Vector3.zero;
		}
		return direction * _distance.Value;
	}

	// The aiming test: blocked from where the fighter stands, but clear leaning out over the edge.
	[Feature(Features.WallLean, Features.WallLeanInfo)]
	[HarmonyPatch(typeof(ProjectileAiming), nameof(ProjectileAiming.IsProjectileObstructed))]
	private static class ObstructedPatch
	{
		private static void Postfix(ProjectileAiming __instance, Vector3 projectileOrigin, LayerMask layerMask, ref bool __result)
		{
			if (!__result || _inner || __instance._parent == null || __instance.Target == null)
			{
				return;
			}
			Vector3 lean = Lean(__instance._parent.gameObject, __instance.GetAnticipatedTargetPosition());
			if (lean == Vector3.zero)
			{
				return;
			}
			_inner = true;
			try
			{
				__result = __instance.IsProjectileObstructed(projectileOrigin + lean, layerMask);
			}
			finally
			{
				_inner = false;
			}
		}
	}

	// The shot itself starts out over the edge.
	[Feature(Features.WallLean, Features.WallLeanInfo)]
	[HarmonyPatch(typeof(ProjectileWeapon), nameof(ProjectileWeapon.Fire))]
	private static class FirePatch
	{
		private static void Prefix(ref Vector3 origin, ref Vector3 aimOrigin, Vector3 target, GameObject source, GameObject thrown)
		{
			if (thrown != null)
			{
				return;
			}
			Vector3 lean = Lean(source, target);
			origin += lean;
			aimOrigin += lean;
		}
	}
}
