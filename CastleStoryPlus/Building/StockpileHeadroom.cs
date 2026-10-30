using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Game.Semantique;
using CastleStoryPlus.Core;
using CastleStoryPlus.Upgrades;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// A stockpile's goods are piled in layers of about a block: the game's 2 layers fill the 2 blocks its volume keeps
// free, and the Stacking research piles up to 4. What is over a stockpile decides how high it may pile:
// - Its room: the Stacking room is cut to the layers that fit under the first solid voxel over it (a floor 3 blocks
//   up leaves 3 layers, the room of 1.5 times the game's). Workers then fill it only that far; goods already over
//   that stay until taken.
// - Building over it: a block, plank or other blueprint cannot be placed in the blocks the goods stand in now (over
//   the 2 the stockpile keeps free itself). Over an empty or low stockpile it can, and the room then shrinks.
// Worked out every few seconds with the Stacking check (Stacking.Update), on every machine; nothing is saved.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
internal static class StockpileHeadroom
{
	// Layers the game piles in the blocks its volume keeps free.
	internal const int BaseLayers = 2;

	// The most layers Stacking piles.
	internal const int MostLayers = 4;

	// The cells over stockpiles their goods stand in now, with the stockpile.
	private static readonly Dictionary<XYZ, Recepteur> Goods = new Dictionary<XYZ, Recepteur>();

	private static readonly Dictionary<Recepteur, List<XYZ>> GoodsOf = new Dictionary<Recepteur, List<XYZ>>();

	private static void Enable()
	{
		GameSession.OnLeave(() =>
		{
			Goods.Clear();
			GoodsOf.Clear();
		});
	}

	// The body's columns: per (x, z) the lowest cell of the stockpile's own volume.
	private static List<XYZ> Floor(Recepteur recepteur)
	{
		List<XYZ> floor = new List<XYZ>();
		Volume volume = recepteur.GetComponent<Volume>();
		if (volume == null)
		{
			return floor;
		}
		Dictionary<long, int> lowest = new Dictionary<long, int>();
		foreach (XYZ cell in volume.obstacles)
		{
			long column = ((long)cell.x << 32) ^ (uint)cell.z;
			if (!lowest.TryGetValue(column, out int y) || cell.y < y)
			{
				lowest[column] = cell.y;
			}
		}
		foreach (KeyValuePair<long, int> column in lowest)
		{
			floor.Add(new XYZ((int)(column.Key >> 32), column.Value, (int)(uint)column.Key));
		}
		return floor;
	}

	// How many layers fit over the stockpile: BaseLayers, plus every free level over its body up to MostLayers.
	internal static int Layers(Recepteur recepteur)
	{
		List<XYZ> floor = Floor(recepteur);
		if (floor.Count == 0)
		{
			return MostLayers;
		}
		for (int layer = BaseLayers; layer < MostLayers; layer++)
		{
			foreach (XYZ cell in floor)
			{
				if (!Voxel.IsOpen(new XYZ(cell.x, cell.y + layer, cell.z)))
				{
					return layer;
				}
			}
		}
		return MostLayers;
	}

	// The Stacking factor (1 + the research's value) cut to the layers that fit.
	internal static float Factor(Recepteur recepteur, float factor)
	{
		if (factor <= 1f)
		{
			return factor;
		}
		return Mathf.Min(factor, Layers(recepteur) / (float)BaseLayers);
	}

	// Marks the cells the goods stand in now: as many layers as the fullest resource fills, over the BaseLayers.
	internal static void Track(Recepteur recepteur)
	{
		int layers = BaseLayers;
		foreach (KeyValuePair<System.Type, Adjectif> entry in recepteur.ContentDescription.DicoAdjectif)
		{
			if (!(entry.Value is Ressource resource))
			{
				continue;
			}
			int original = Stacking.OriginalRoom(recepteur, entry.Key);
			int held = entry.Value.quantifiable.valeur;
			if (original <= 0 || held <= 0)
			{
				continue;
			}
			layers = Mathf.Max(layers, Mathf.CeilToInt(BaseLayers * (float)held / original - 0.001f));
		}
		layers = Mathf.Min(layers, MostLayers);
		Forget(recepteur);
		if (layers <= BaseLayers)
		{
			return;
		}
		List<XYZ> cells = new List<XYZ>();
		foreach (XYZ cell in Floor(recepteur))
		{
			for (int layer = BaseLayers; layer < layers; layer++)
			{
				XYZ at = new XYZ(cell.x, cell.y + layer, cell.z);
				Goods[at] = recepteur;
				cells.Add(at);
			}
		}
		GoodsOf[recepteur] = cells;
	}

	internal static void Forget(Recepteur recepteur)
	{
		if (!GoodsOf.TryGetValue(recepteur, out List<XYZ> cells))
		{
			return;
		}
		foreach (XYZ cell in cells)
		{
			if (Goods.TryGetValue(cell, out Recepteur owner) && owner == recepteur)
			{
				Goods.Remove(cell);
			}
		}
		GoodsOf.Remove(recepteur);
	}

	// Stockpiles gone since the last check.
	internal static void Prune()
	{
		List<Recepteur> gone = null;
		foreach (Recepteur recepteur in GoodsOf.Keys)
		{
			if (recepteur == null)
			{
				if (gone == null)
				{
					gone = new List<Recepteur>();
				}
				gone.Add(recepteur);
			}
		}
		if (gone == null)
		{
			return;
		}
		foreach (Recepteur recepteur in gone)
		{
			Forget(recepteur);
		}
	}

	// Whether a stockpile's goods stand in the cell (not the volume being checked itself).
	internal static bool Blocks(XYZ cell, GameObject self)
	{
		if (Goods.Count == 0 || !Goods.TryGetValue(cell, out Recepteur owner))
		{
			return false;
		}
		return owner != null && (self == null || owner.gameObject != self);
	}
}

// Blueprints are not placed (and are obstructed) in the cells a stockpile's goods stand in.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(VUtil), nameof(VUtil.IsObstructed), typeof(XYZ), typeof(VPropertyMask), typeof(Faction), typeof(IVolume))]
internal static class StockpileHeadroomObstructionPatch
{
	private static void Postfix(XYZ i, IVolume excludedVolume, ref bool __result)
	{
		if (!__result && StockpileHeadroom.Blocks(i, excludedVolume?.GameObject))
		{
			__result = true;
		}
	}
}

[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(VUtil), nameof(VUtil.IsObstructed), typeof(XYZ), typeof(VPropertyMask), typeof(Faction), typeof(List<IVolume>))]
internal static class StockpileHeadroomObstructionListPatch
{
	private static void Postfix(XYZ i, ref bool __result)
	{
		if (!__result && StockpileHeadroom.Blocks(i, null))
		{
			__result = true;
		}
	}
}
