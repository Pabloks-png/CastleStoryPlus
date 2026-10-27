using BepInEx.Configuration;
using Brix.Engine.Nature;
using Brix.External.Factories;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using Brix.Legacy;
using CastleStoryPlus.Core;
using CastleStoryPlus.Upgrades;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Metallurgy;

// Iron in the ground gives more: every raw iron that comes out of iron ore a worker digs, or out of an iron deposit
// a worker mines, has a [RichIron] Bonus (25%) chance of coming with one more, so iron yields a quarter more on
// average. On top of Efficient Mining's research bonus. Host only: the extra items are spawned like the game's own.
[Feature(Features.RichIron, Features.RichIronInfo)]
internal static class RichIron
{
	private static ConfigEntry<float> _bonus;

	// A worker is breaking terrain right now.
	private static bool _breaking;

	private static void Enable()
	{
		_bonus = Plugin.Cfg.Bind("RichIron", "Bonus", 0.25f, new ConfigDescription("Extra raw iron from iron ore and iron deposits, on average (0.25: a quarter more).", new AcceptableValueRange<float>(0f, 2f)));
	}

	// count raw iron came out: some more, each with the bonus' chance (above 1: whole ones plus a chance).
	private static void More(Vector3 position, int count)
	{
		float bonus = _bonus.Value;
		int extra = 0;
		for (int i = 0; i < count; i++)
		{
			extra += Mathf.FloorToInt(bonus) + ((Random.value < bonus - Mathf.Floor(bonus)) ? 1 : 0);
		}
		EfficientMining.Spawn(position, extra, RawResources.RawIron);
	}

	[Feature(Features.RichIron, Features.RichIronInfo)]
	[HarmonyPatch(typeof(TerrainDamageReceiver), nameof(TerrainDamageReceiver.ReceiveAttack))]
	private static class TerrainPatch
	{
		private static void Prefix(GameObject attacker)
		{
			_breaking = NetworkServer.active && attacker != null && attacker.GetComponent<Labor>() != null;
		}

		private static System.Exception Finalizer(System.Exception __exception)
		{
			_breaking = false;
			return __exception;
		}
	}

	[Feature(Features.RichIron, Features.RichIronInfo)]
	[HarmonyPatch(typeof(Garnotte), nameof(Garnotte.Nouvelle))]
	private static class SpawnPatch
	{
		private static void Postfix(Vector3 position, int qte, Factory.AssetKey assetKey)
		{
			if (_breaking && !EfficientMining.Spawning && assetKey == RawResources.RawIron)
			{
				More(position, qte);
			}
		}
	}

	// A hit on an iron deposit gives one raw iron (MineralRegistry.Harvest), spawned a moment later on every machine.
	[Feature(Features.RichIron, Features.RichIronInfo)]
	[HarmonyPatch(typeof(NatureHarvestGoal), "HarvestHitAction")]
	private static class DepositPatch
	{
		private static void Prefix(NatureHarvestGoal __instance, Labor labor)
		{
			NatureData data = __instance.NData;
			if (!NetworkServer.active || labor == null || data == null || !data.Registered() || IronRegistry.GetMineral(data.RootPos) == null)
			{
				return;
			}
			More(data.RootPos.ToVector3() + new Vector3(0f, 0.5f, 0f), 1);
		}
	}
}
