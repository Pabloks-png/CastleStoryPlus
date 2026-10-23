using System;
using System.Collections.Generic;
using Brix.Assets;
using Brix.Engine;
using Brix.UI.Builder.Menu;
using Brix.UI.Builder.Panel;
using Brix.UI.Icons;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine.UI;

namespace CastleStoryPlus.WorldGen;

// The random world's choices on the new game screen, under the mode and preset: world style, size, biome, how many
// trees, boulders and ores, and the seed, a UUID (the button rolls a new one, the field takes a typed or pasted one). Each change makes the world again in the background and
// its picture replaces the map's thumbnail when it is done.
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch(typeof(BaseMapStarterMenu), "Footer", MethodType.Getter)]
internal static class RandomWorldMenu
{
	private static BaseMapStarterMenu _open;

	private static readonly string[] StyleNames = { "ARCHIPELAGO", "CONTINENT", "HIGHLANDS" };

	private static readonly string[] SizeNames = { "SMALL", "MEDIUM", "LARGE" };

	private static readonly string[] BiomeNames = { "FOREST", "WINTER", "DESERT" };

	private static readonly string[] AmountNames = { "NONE", "FEW", "NORMAL", "MANY", "LOTS" };

	internal static void RefreshOpen()
	{
		if (_open != null && _open.Opened)
		{
			_open.Refresh();
		}
	}

	private static void Postfix(BaseMapStarterMenu __instance, ref PanelList __result)
	{
		if (__result == null)
		{
			return;
		}
		BaseMapStarterMenu menu = __instance;
		_open = menu;
		DropdownPanel.Variant variant = menu.IsReskin ? DropdownPanel.Variant.StandardBigReskin : DropdownPanel.Variant.StandardBig;
		List<IPanel> panels = new List<IPanel>
		{
			Choice(menu, variant, "World", StyleNames, () => (int)RandomWorld.Settings.Style, (WorldSettings s, int v) =>
			{
				s.Style = (WorldStyle)v;
				return s;
			}),
			Choice(menu, variant, "Size", SizeNames, () => (int)RandomWorld.Settings.Size, (WorldSettings s, int v) =>
			{
				s.Size = (WorldSize)v;
				return s;
			}),
			Choice(menu, variant, "Biome", BiomeNames, () => (int)RandomWorld.Settings.Biome, (WorldSettings s, int v) =>
			{
				s.Biome = (WorldBiome)v;
				return s;
			}),
			Choice(menu, variant, "Trees", AmountNames, () => (int)RandomWorld.Settings.Trees, (WorldSettings s, int v) =>
			{
				s.Trees = (WorldAmount)v;
				return s;
			}),
			Choice(menu, variant, "Boulders", AmountNames, () => (int)RandomWorld.Settings.Boulders, (WorldSettings s, int v) =>
			{
				s.Boulders = (WorldAmount)v;
				return s;
			}),
			Choice(menu, variant, "Surface ore", AmountNames, () => (int)RandomWorld.Settings.SurfaceOre, (WorldSettings s, int v) =>
			{
				s.SurfaceOre = (WorldAmount)v;
				return s;
			}),
			Choice(menu, variant, "Deep ore", AmountNames, () => (int)RandomWorld.Settings.DeepOre, (WorldSettings s, int v) =>
			{
				s.DeepOre = (WorldAmount)v;
				return s;
			}),
			SeedButton(menu),
			SeedField(menu)
		};
		int at = Math.Min(3, __result.Count);
		for (int i = 0; i < panels.Count; i++)
		{
			__result.Insert(at + i, panels[i]);
		}
	}

	private static bool Shown(BaseMapStarterMenu menu)
	{
		bool shown = RandomWorld.IsRandomWorld(menu.DisplayedMap) && menu.IsNewGame && !menu.IsExistingAsset && !menu.IsWorldEditorMenu && !menu.IsCreditIslandMenu && menu.IsNotMultiplayerOrIsHost;
		if (shown)
		{
			RandomWorld.Shown(menu.DisplayedMap);
		}
		return shown;
	}

	private static void Change(BaseMapStarterMenu menu, WorldSettings settings)
	{
		RandomWorld.Change(settings);
		MenuSound.PlaySound(menu.SoundPrefix, MenuSound.Suffix.Select);
		menu.Refresh();
	}

	private static IPanel Choice(BaseMapStarterMenu menu, DropdownPanel.Variant variant, string key, string[] names, Func<int> current, Func<WorldSettings, int, WorldSettings> apply)
	{
		Action<DropdownPanelComponent> refresh = (DropdownPanelComponent panel) =>
		{
			if (!Shown(menu))
			{
				panel.gameObject.SetActive(false);
				return;
			}
			panel.gameObject.SetActive(true);
			panel._keyLabel.text = key;
			panel._keyLabel.color = BaseMenu.Colors.Black;
			panel._valueLabel.color = BaseMenu.Colors.Black;
			panel._arrowImage.color = BaseMenu.Colors.Black;
			panel._arrowImage.gameObject.SetActive(true);
			panel._dropdown.interactable = true;
			panel._dropdown.onValueChanged.RemoveAllListeners();
			panel._dropdown.options = new List<Dropdown.OptionData>();
			foreach (string name in names)
			{
				panel._dropdown.options.Add(new Dropdown.OptionData(name));
			}
			int value = current();
			panel._dropdown.value = value;
			panel._valueLabel.text = names[value];
			panel._dropdown.onValueChanged.AddListener((int chosen) =>
			{
				panel._valueLabel.text = names[chosen];
				if (chosen != current())
				{
					Change(menu, apply(RandomWorld.Settings, chosen));
				}
			});
		};
		return DropdownPanel.New(refresh, refresh).As(variant);
	}

	// The seed's UUID; a click rolls a new one.
	private static IPanel SeedButton(BaseMapStarterMenu menu)
	{
		Action<LegacyButtonPanelComponent> reset = (LegacyButtonPanelComponent panel) =>
		{
			panel._button.onClick.RemoveAllListeners();
			panel._button.onClick.AddListener(() =>
			{
				WorldSettings settings = RandomWorld.Settings;
				settings.Seed = RandomWorld.NewSeed();
				Change(menu, settings);
			});
		};
		Action<LegacyButtonPanelComponent> refresh = (LegacyButtonPanelComponent panel) =>
		{
			if (!Shown(menu))
			{
				panel.gameObject.SetActive(false);
				return;
			}
			panel.gameObject.SetActive(true);
			panel._keyLabel.text = RandomWorld.Busy ? "Seed (making the world...)" : "Seed (click for a new one)";
			panel._keyLabel.color = BaseMenu.Colors.Black;
			panel._label.text = RandomWorld.Settings.Seed.ToUpperInvariant();
			panel._label.color = BaseMenu.Colors.Black;
			panel._selectionImage.color = BaseMenu.Colors.Black;
			panel._selectionImage.sprite = IconKeys._UI_Big_Reload.Get64();
			panel._selectionImage.gameObject.SetActive(true);
			panel._button.interactable = true;
		};
		LegacyButtonPanelComponent.BasePanel button = LegacyButtonPanelComponent.Build(reset, refresh).As(LegacyButtonPanelComponent.BasePanel.Variant.LegacyReskin_DblTxt_RightIcon);
		button.IgnoreWidgets();
		return button;
	}

	// A seed typed or pasted in: a UUID, or any other text.
	private static IPanel SeedField(BaseMapStarterMenu menu)
	{
		Action<InputFieldPanelComponent> reset = (InputFieldPanelComponent panel) =>
		{
			panel._inputField.onEndEdit.RemoveAllListeners();
			panel._inputField.onEndEdit.AddListener((string text) =>
			{
				if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
				{
					return;
				}
				WorldSettings settings = RandomWorld.Settings;
				string seed = text.Trim().ToLowerInvariant();
				panel._inputField.text = string.Empty;
				if (seed != settings.Seed)
				{
					settings.Seed = seed;
					Change(menu, settings);
				}
			});
		};
		Action<InputFieldPanelComponent> refresh = (InputFieldPanelComponent panel) =>
		{
			bool shown = Shown(menu);
			panel.gameObject.SetActive(shown);
			if (shown)
			{
				panel._inputFieldPlaceholderText.text = "TYPE OR PASTE A SEED (UUID)...";
			}
		};
		return InputFieldPanelComponent.Build(reset, refresh).As(menu.IsReskin ? InputFieldPanelComponent.BasePanel.Variant.ReskinLeft : InputFieldPanelComponent.BasePanel.Variant.Standard);
	}
}

// The menu shows the newest world's picture instead of the thumbnail file read before it was made.
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch(typeof(Asset_Map), nameof(Asset_Map.Disk_ReadThumbnail))]
internal static class RandomWorldThumbnailPatch
{
	private static bool Prefix(Asset_Map __instance, ref UnityEngine.Texture2D __result)
	{
		if (!RandomWorld.IsRandomWorld(__instance) || RandomWorld.Preview == null)
		{
			return true;
		}
		__instance.thumbnail = RandomWorld.Preview;
		__result = RandomWorld.Preview;
		return false;
	}
}

// Starting the random world waits for the world being made and makes sure the files are the chosen settings'.
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch(typeof(GameParam), nameof(GameParam.StartGame))]
internal static class RandomWorldStartPatch
{
	private static void Prefix()
	{
		RandomWorld.EnsureWritten(GameParam.Map);
	}
}

[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch(typeof(GameLoader), nameof(GameLoader.LoadNewGameFromGameParams))]
internal static class RandomWorldLoadPatch
{
	private static void Prefix()
	{
		RandomWorld.EnsureWritten(GameParam.Map);
	}
}
