using System.Collections.Generic;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Game.Semantique;
using CastleStoryPlus.Core;
using CastleStoryPlus.Upgrades;
using HarmonyLib;
using Smooth.Slinq;
using UnityEngine;

namespace CastleStoryPlus.Building;

// How high a stockpile may pile its goods depends on what is over it. Its volume keeps 2 blocks free, about what a
// full stockpile of the game reaches (2.2 blocks over its pallet's foot); with the Stacking research it piles up to
// twice that (4.4 blocks: 4 layers of blocks, crates twice as tall) and stuck through a floor built over it.
// - Its room: the Stacking factor is cut so a full stockpile reaches no higher than the first solid voxel or built
//   thing (a plank floor, a beam, a building) over it, allowing the small overlap the game's own stockpiles have under
//   a floor 2 blocks up. Workers then fill it only that far; goods already over that stay until taken.
// - Building over it: a block, plank or other blueprint cannot be placed in the blocks its goods stand in now (over
//   the 2 the stockpile keeps free itself). Over an empty or low stockpile it can, and the room then shrinks.
// Worked out every few seconds with the Stacking check (Stacking.Update), on every machine; nothing is saved.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
internal static class StockpileHeadroom
{
	// The blocks the stockpile's volume keeps free over its foot.
	internal const int OwnBlocks = 2;

	// The height over its foot a full stockpile reaches with the game's own room (measured: 2.2 for blocks and crates).
	internal const float FullHeight = 2.2f;

	// How far goods may reach into the block over them (the game's full stockpiles reach 0.2 into a floor 2 blocks up).
	internal const float Overlap = 0.25f;

	// The highest block over the foot that Stacking's tallest pile (twice the room) can reach.
	private const int HighestBlock = 4;

	// The cells over stockpiles their goods are drawn in now, with the stockpile.
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

	// The free height over the stockpile's foot: the first block over its own ones that holds something, else more
	// than the tallest pile needs.
	internal static int FreeBlocks(Recepteur recepteur)
	{
		List<XYZ> floor = Floor(recepteur);
		if (floor.Count == 0)
		{
			return HighestBlock + 1;
		}
		GameObject self = recepteur.gameObject;
		for (int block = OwnBlocks; block <= HighestBlock; block++)
		{
			foreach (XYZ cell in floor)
			{
				if (Taken(new XYZ(cell.x, cell.y + block, cell.z), self))
				{
					return block;
				}
			}
		}
		return HighestBlock + 1;
	}

	// A solid voxel, or something built (a plank floor, a beam, a building) other than the stockpile itself.
	private static bool Taken(XYZ cell, GameObject self)
	{
		if (!Voxel.IsOpen(cell))
		{
			return true;
		}
		return VolumeRegistry.VolumesWithPropertyAt(cell, VProperty.obstacle).Slinq().Where((IVolume v, GameObject _self) => !(v is BlueprintVolume) && v.GameObject != _self, self).Any()
			|| VolumeRegistry.VolumesWithPropertyAt(cell, VProperty.occupy).Slinq().Where((IVolume v, GameObject _self) => !(v is BlueprintVolume) && v.GameObject != _self, self).Any();
	}

	// The Stacking factor (1 + the research's value) cut so a full stockpile fits under what is over it.
	internal static float Factor(Recepteur recepteur, float factor)
	{
		if (factor <= 1f)
		{
			return factor;
		}
		float fits = (FreeBlocks(recepteur) + Overlap) / FullHeight;
		return Mathf.Max(1f, Mathf.Min(factor, fits));
	}

	// Marks the cells over its own blocks that the stockpile's goods stand in now: as high as the fullest resource
	// piles (FullHeight for the game's room). An empty crate's frame does not count, so a floor can still be built over
	// a stockpile that holds little, and its room then shrinks (and its crate with it) to what fits.
	internal static void Track(Recepteur recepteur)
	{
		Forget(recepteur);
		float fill = 0f;
		foreach (KeyValuePair<System.Type, Adjectif> entry in recepteur.ContentDescription.DicoAdjectif)
		{
			if (!(entry.Value is Ressource))
			{
				continue;
			}
			int original = Stacking.OriginalRoom(recepteur, entry.Key);
			if (original > 0)
			{
				fill = Mathf.Max(fill, (float)entry.Value.quantifiable.valeur / original);
			}
		}
		float height = FullHeight * fill - Overlap;
		if (height <= OwnBlocks)
		{
			return;
		}
		List<XYZ> cells = new List<XYZ>();
		foreach (XYZ cell in Floor(recepteur))
		{
			for (int block = OwnBlocks; block < height && block <= HighestBlock; block++)
			{
				XYZ at = new XYZ(cell.x, cell.y + block, cell.z);
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
