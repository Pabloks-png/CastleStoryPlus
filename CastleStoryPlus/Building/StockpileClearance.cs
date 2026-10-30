using System;
using System.Collections.Generic;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// A stockpile keeps the room of its body free of blocks: the game's pallet (and its blueprint) has a volume of 2
// blocks high, and a block or another building cannot be placed in it. With the Stacking research the goods are piled
// up to 4 layers and the crates are as much taller (StackingVisual), so a full stockpile with a floor 2 or 3 blocks
// over it stuck through that floor. The body of the pallet's volume (and so of the large stockpile cloned from it) is
// now Height blocks high: nothing can be built in the 4 blocks over a stockpile, and a stockpile cannot be placed
// with less room than that over it. The added layers copy the body's top layer, so workers stand and take as before.
// Stockpiles placed before keep their place; what is already built over one stays.
[Feature(Features.Upgrades, Features.UpgradesInfo)]
[HarmonyPatch(typeof(Factory), nameof(Factory.Awake))]
internal static class StockpileClearance
{
	internal const int Height = 4;

	// Factory name and template name of the pallet's building and blueprint.
	private static readonly string[][] Templates =
	{
		new[] { "ObjetsDynamiques", "Palette" },
		new[] { "Blueprints", "Pallet" },
	};

	// Volumes already raised (a template is raised once, also when its factory wakes again).
	private static readonly HashSet<VolumeDataComponent> Raised = new HashSet<VolumeDataComponent>();

	// Before LargeStockpile clones the pallet, so the large one gets the raised volume too.
	[HarmonyPriority(Priority.First)]
	private static void Postfix()
	{
		foreach (string[] template in Templates)
		{
			TryRaise(template[0], template[1]);
		}
	}

	private static void TryRaise(string factoryName, string templateName)
	{
		if (!Factory.Factories.TryGetValue(factoryName, out Factory factory) || factory == null)
		{
			return;
		}
		try
		{
			if (factory.cachedTemplates.Count == 0)
			{
				factory.ResetTemplateCache();
			}
			if (!factory.cachedTemplates.ContainsKey(templateName))
			{
				return;
			}
			VolumeDataComponent volume = factory.GetTemplate(templateName).GetComponent<VolumeDataComponent>();
			if (volume == null || !Raised.Add(volume))
			{
				return;
			}
			int added = Raise(volume.IndexedVolumetricProperties);
			Plugin.Log.LogInfo("StockpileClearance: " + factoryName + "/" + templateName + " body raised to " + Height + " blocks (" + added + " cells added)");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("StockpileClearance: could not raise " + factoryName + "/" + templateName + ": " + ex);
		}
	}

	// Each column of the body (obstacle cells of the building, ghost cells of the blueprint; not the access cells
	// around it where workers stand, nor the support layers under it) gets cells up to Height above its lowest body
	// cell, each a copy of the column's top cell.
	private static int Raise(Dictionary<XYZ, long> cells)
	{
		const long body = (long)(VPropertyMask.obstacle | VPropertyMask.fantome);
		Dictionary<long, XYZ> bottom = new Dictionary<long, XYZ>();
		Dictionary<long, XYZ> top = new Dictionary<long, XYZ>();
		foreach (KeyValuePair<XYZ, long> cell in cells)
		{
			if ((cell.Value & body) == 0)
			{
				continue;
			}
			long column = ((long)cell.Key.x << 32) ^ (uint)cell.Key.z;
			if (!bottom.TryGetValue(column, out XYZ low) || cell.Key.y < low.y)
			{
				bottom[column] = cell.Key;
			}
			if (!top.TryGetValue(column, out XYZ high) || cell.Key.y > high.y)
			{
				top[column] = cell.Key;
			}
		}
		int added = 0;
		foreach (KeyValuePair<long, XYZ> column in top)
		{
			XYZ high = column.Value;
			long mask = cells[high];
			int last = bottom[column.Key].y + Height - 1;
			for (int y = high.y + 1; y <= last; y++)
			{
				XYZ at = new XYZ(high.x, y, high.z);
				if (!cells.ContainsKey(at))
				{
					cells[at] = mask;
					added++;
				}
			}
		}
		return added;
	}
}
