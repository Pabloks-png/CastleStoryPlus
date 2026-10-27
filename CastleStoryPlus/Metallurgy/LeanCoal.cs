using BepInEx.Configuration;
using Brix.Engine.Nature;
using Brix.External.Factories;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using Brix.Legacy;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Metallurgy;

// Mining gives less coal: every coal that comes out of a coal vein a worker digs, or out of a coal deposit a worker
// mines, is dropped with a [LeanCoal] Reduction (30%) chance, so mining yields 30% less coal on average. Coal from
// other sources (beasts, the market, the charcoal kiln) is not touched. Host only: items are spawned on the host.
[Feature(Features.LeanCoal, Features.LeanCoalInfo)]
internal static class LeanCoal
{
	private static ConfigEntry<float> _reduction;

	// A worker is breaking terrain, or a deposit is harvested, right now.
	private static bool _mining;

	private static void Enable()
	{
		_reduction = Plugin.Cfg.Bind("LeanCoal", "Reduction", 0.3f, new ConfigDescription("Share of the coal from coal veins and coal deposits that is lost (0.3: 30% less on average).", new AcceptableValueRange<float>(0f, 1f)));
	}

	private static System.Exception Done(System.Exception exception)
	{
		_mining = false;
		return exception;
	}

	[Feature(Features.LeanCoal, Features.LeanCoalInfo)]
	[HarmonyPatch(typeof(TerrainDamageReceiver), nameof(TerrainDamageReceiver.ReceiveAttack))]
	private static class TerrainPatch
	{
		private static void Prefix(GameObject attacker)
		{
			_mining = NetworkServer.active && attacker != null && attacker.GetComponent<Labor>() != null;
		}

		private static System.Exception Finalizer(System.Exception __exception)
		{
			return Done(__exception);
		}
	}

	// Before CoalDeposits' own prefix, which spawns the coal.
	[Feature(Features.LeanCoal, Features.LeanCoalInfo)]
	[HarmonyPatch(typeof(MineralRegistry), nameof(MineralRegistry.Harvest))]
	private static class DepositPatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix()
		{
			_mining = NetworkServer.active;
		}

		private static System.Exception Finalizer(System.Exception __exception)
		{
			return Done(__exception);
		}
	}

	[Feature(Features.LeanCoal, Features.LeanCoalInfo)]
	[HarmonyPatch(typeof(Garnotte), nameof(Garnotte.Nouvelle))]
	private static class SpawnPatch
	{
		private static bool Prefix(ref int qte, Factory.AssetKey assetKey)
		{
			if (!_mining || assetKey != Metallurgy.CoalKey)
			{
				return true;
			}
			float reduction = _reduction.Value;
			int kept = 0;
			for (int i = 0; i < qte; i++)
			{
				if (Random.value >= reduction)
				{
					kept++;
				}
			}
			qte = kept;
			return kept > 0;
		}
	}
}
