using System;
using System.Collections.Generic;
using Brix.Game.Semantique;
using Brix.IO.Serialization.Contracts;
using CastleStoryPlus.Core;
using HarmonyLib;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace CastleStoryPlus.Upgrades;

// A stockpile's own Stacking tier in place of its team's, for the DevTools test world: a row of stockpiles for every
// tier, side by side, while the team has everything researched. Saved as an extra property on the stockpile's existing
// Recepteur component ("stackingTier", only on stockpiles that have one; the unmodded game ignores it). Not synced:
// a client sees the team's tier there (the test world is a single-player sandbox).
internal static class StockpileTiers
{
	private static readonly Dictionary<GameObject, int> Tiers = new Dictionary<GameObject, int>();

	internal static bool TryGet(GameObject storage, out int tier)
	{
		tier = 0;
		return storage != null && Tiers.TryGetValue(storage, out tier);
	}

	internal static void Set(Recepteur storage, int tier)
	{
		if (storage != null)
		{
			Tiers[storage.gameObject] = Mathf.Clamp(tier, 0, UpgradeLines.MaxTier);
		}
	}

	internal static void Clear(Recepteur storage)
	{
		if (storage != null)
		{
			Tiers.Remove(storage.gameObject);
		}
	}

	// Entries of destroyed stockpiles.
	internal static void Prune()
	{
		List<GameObject> gone = null;
		foreach (GameObject storage in Tiers.Keys)
		{
			if (storage == null)
			{
				(gone ?? (gone = new List<GameObject>())).Add(storage);
			}
		}
		if (gone != null)
		{
			foreach (GameObject storage in gone)
			{
				Tiers.Remove(storage);
			}
		}
	}
}

[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(DefaultGameContractResolver), nameof(DefaultGameContractResolver.GenerateDefaultMonoBehaviourContract))]
internal static class StockpileTiersSave
{
	private const string Property = "stackingTier";

	private class TierProvider : IValueProvider
	{
		public object GetValue(object target)
		{
			Recepteur storage = target as Recepteur;
			return (storage != null && StockpileTiers.TryGet(storage.gameObject, out int tier)) ? tier : -1;
		}

		public void SetValue(object target, object value)
		{
			int tier = Convert.ToInt32(value);
			if (tier >= 0)
			{
				StockpileTiers.Set(target as Recepteur, tier);
			}
		}
	}

	private static void Postfix(Type objectType, JsonObjectContract __result)
	{
		if (objectType != typeof(Recepteur) || __result == null || __result.Properties.Contains(Property))
		{
			return;
		}
		__result.Properties.AddProperty(new JsonProperty
		{
			PropertyName = Property,
			UnderlyingName = Property,
			PropertyType = typeof(int),
			DeclaringType = typeof(Recepteur),
			ValueProvider = new TierProvider(),
			ShouldSerialize = (object target) => target is Recepteur storage && StockpileTiers.TryGet(storage.gameObject, out int _),
			Readable = true,
			Writable = true
		});
	}
}
