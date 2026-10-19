using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Building;

// Wood blocks (logs) can be built out sideways from other wood: a wood block with nothing under it holds when it
// touches, at its side, wood that leads within [WoodOverhang] MaxOverhang blocks to wood that stands on something.
// So a row of logs can hang up to 5 blocks out into the air from a supported log. Blueprints follow the same rule
// (a hanging blueprint is valid next to a wood blueprint, and buildable once the wood it hangs from is built), so
// workers build such a row outward from its supported end.
[Feature(Features.WoodOverhang, Features.WoodOverhangInfo)]
internal static class WoodOverhang
{
	internal static ConfigEntry<int> MaxOverhang;

	private static readonly XYZ[] Sides = new XYZ[4]
	{
		new XYZ(1, 0, 0),
		new XYZ(-1, 0, 0),
		new XYZ(0, 0, 1),
		new XYZ(0, 0, -1)
	};

	// Set while the game's own rule is asked for the wood a block hangs from.
	private static bool _inner;

	private static void Enable()
	{
		MaxOverhang = Plugin.Cfg.Bind("WoodOverhang", "MaxOverhang", 5, new ConfigDescription("How many wood blocks may hang out sideways into the air from a supported wood block.", new AcceptableValueRange<int>(1, 10)));
	}

	internal static bool IsWood(AbstractValidator validator)
	{
		return validator is LogBlockValidator || validator is LogBPValidator;
	}

	// Whether the block hangs from supported wood within MaxOverhang. built: only placed wood counts (a blueprint
	// becomes buildable); otherwise wood blueprints count as well (a blueprint is valid).
	internal static bool HangsFromWood(ref ValidationData vData, bool built)
	{
		if (_inner || vData.Volume == null)
		{
			return false;
		}
		IVolume self = vData.Volume;
		Faction faction = vData.Faction;
		int max = (MaxOverhang != null) ? MaxOverhang.Value : 5;
		HashSet<IVolume> seen = new HashSet<IVolume> { self };
		List<IVolume> frontier = new List<IVolume> { self };
		for (int depth = 1; depth <= max && frontier.Count > 0; depth++)
		{
			List<IVolume> next = new List<IVolume>();
			foreach (IVolume volume in frontier)
			{
				foreach (XYZ cell in Cells(volume))
				{
					foreach (XYZ side in Sides)
					{
						IVolume wood = WoodAt(cell + side, built, faction);
						if (wood == null || !seen.Add(wood))
						{
							continue;
						}
						if (StandsOnItsOwn(wood))
						{
							return true;
						}
						next.Add(wood);
					}
				}
			}
			frontier = next;
		}
		return false;
	}

	private static List<XYZ> Cells(IVolume volume)
	{
		List<XYZ> cells = new List<XYZ>();
		foreach (XYZ cell in volume.GetCases(VPropertyMask.occupy | VPropertyMask.fantome))
		{
			cells.Add(cell);
		}
		return cells;
	}

	// Placed wood at the cell, or (unless built) a wood blueprint of the same faction.
	private static IVolume WoodAt(XYZ cell, bool built, Faction faction)
	{
		foreach (IVolume volume in VolumeRegistry.VolumesWithPropertyAt(cell, VProperty.occupy))
		{
			if (IsWoodVolume(volume, faction))
			{
				return volume;
			}
		}
		if (built)
		{
			return null;
		}
		foreach (IVolume volume in VolumeRegistry.VolumesWithPropertyAt(cell, VProperty.fantome))
		{
			if (IsWoodVolume(volume, faction))
			{
				return volume;
			}
		}
		return null;
	}

	private static bool IsWoodVolume(IVolume volume, Faction faction)
	{
		if (volume == null || volume.GameObject == null)
		{
			return false;
		}
		ValidatorComponent component = volume.GameObject.GetComponent<ValidatorComponent>();
		if (component == null || !IsWood(component.Validator))
		{
			return false;
		}
		ValidationData data = volume.VData;
		return faction == null || data.Faction == null || data.Faction == faction;
	}

	// The game's own rule: the wood stands on something (not on other hanging wood).
	private static bool StandsOnItsOwn(IVolume volume)
	{
		ValidatorComponent component = volume.GameObject.GetComponent<ValidatorComponent>();
		ValidationData data = volume.VData;
		_inner = true;
		try
		{
			ValidationResult result = component.Validator.Validate(ref data, null);
			return result == ValidationResult.Valid || result == ValidationResult.Buildable;
		}
		finally
		{
			_inner = false;
		}
	}
}

// Valid: a placed wood block stays up, a wood blueprint may be placed.
[Feature(Features.WoodOverhang, Features.WoodOverhangInfo)]
[HarmonyPatch(typeof(AbstractValidator), "IsValid")]
internal static class WoodOverhangValidPatch
{
	private static void Postfix(AbstractValidator __instance, ref ValidationData vData, ref bool __result)
	{
		if (__result || !WoodOverhang.IsWood(__instance))
		{
			return;
		}
		// A placed block hangs only from placed wood; a blueprint may hang from wood blueprints too.
		__result = WoodOverhang.HangsFromWood(ref vData, built: !(__instance is LogBPValidator));
	}
}

// Buildable: the wood the blueprint hangs from is built.
[Feature(Features.WoodOverhang, Features.WoodOverhangInfo)]
[HarmonyPatch(typeof(BlueprintValidator), "IsBuildable")]
internal static class WoodOverhangBuildablePatch
{
	private static void Postfix(BlueprintValidator __instance, ref ValidationData vData, ref bool __result)
	{
		if (__result || !(__instance is LogBPValidator))
		{
			return;
		}
		__result = WoodOverhang.HangsFromWood(ref vData, built: true);
	}
}
