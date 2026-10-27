using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// Quarries are dug out faster ([QuarrySpeed] Speed, 1.3 = 30%): a block takes fewer swings of the pick, the swing
// itself (animation, walking) stays as it is. Each swing in a quarry adds Speed to the worker's swing credit and
// deals one swing's damage for every whole swing in it; the rest carries over to the next swing, so on average a
// swing counts as Speed swings. No more swings are dealt than the block has health left for, so nothing is lost on
// the last swing of a block. Other digging (tunnels, dig and flatten tasks) is not changed.
[Feature(Features.QuarrySpeed, Features.QuarrySpeedInfo)]
[HarmonyPatch(typeof(DigGoal), "ApplyDamage")]
internal static class QuarrySpeed
{
	private static ConfigEntry<float> _speed;

	private static readonly Dictionary<Labor, float> Credit = new Dictionary<Labor, float>();

	private static void Enable()
	{
		_speed = Plugin.Cfg.Bind("QuarrySpeed", "Speed", 1.3f, new ConfigDescription("How many times faster than the game's bricktrons dig out a quarry (1.3 = 30% faster): how many swings of the pick each swing counts as.", new AcceptableValueRange<float>(1f, 3f)));
		GameSession.OnLeave(Credit.Clear);
	}

	private static bool Prefix(DigGoal __instance, Labor labor)
	{
		if (labor == null || !(__instance.project is MineProject) || _speed.Value <= 1f)
		{
			return true;
		}
		DamageReaction expectedReaction = __instance.MustOverkill() ? DamageReaction.ForceOverkill : DamageReaction.Ignored;
		float value = (Voxel.GetBlockInfo(XYZ.FromVector3(__instance.transform.position)).Type != BlockType.Terrain) ? (float)labor.Occupation.Profession.BlockBreaking : (float)labor.Occupation.Profession.TerrainDamage;
		IDamageReceiver receiver = __instance.damageReceiver;
		Credit.TryGetValue(labor, out float credit);
		credit += _speed.Value;
		int swings = Mathf.Max(1, Mathf.FloorToInt(credit));
		if (receiver is TerrainDamageReceiver terrain && value > 0f)
		{
			swings = Mathf.Clamp(Mathf.CeilToInt(terrain.CurrentHP / value), 1, swings);
		}
		// Credit kept from a swing capped by the block's health is used on the next block, but never builds up
		// beyond one swing.
		Credit[labor] = Mathf.Min(credit - swings, 1f);
		receiver.ReceiveAttack(value * swings, DamageType.Siege, expectedReaction, Vector3.zero, Vector3.zero, 0f, labor.gameObject);
		return false;
	}
}
