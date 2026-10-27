using System;
using System.Collections.Generic;
using Brix.Assets;
using Brix.Engine;
using Brix.UI.Builder.Menu;
using Brix.UI.Builder.Panel;
using Brix.UI.Icons;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.WorldGen;

// The random world on the new game screen: its choices are in RandomWorldPanel, in the middle of the screen; this
// keeps track of the open new game menu (the panel shows while it is open on the random world) and makes a change
// (the world is made again in the background, its picture replacing the map's thumbnail when it is done).
[Feature(Features.RandomWorld, Features.RandomWorldInfo)]
[HarmonyPatch(typeof(BaseMapStarterMenu), "Footer", MethodType.Getter)]
internal static class RandomWorldMenu
{
	private static BaseMapStarterMenu _open;

	// An invisible row in each new game menu's sidebar, active while that menu shows the random world: the panel
	// shows while one is active in the scene.
	private static readonly List<KeyValuePair<BaseMapStarterMenu, GameObject>> Markers = new List<KeyValuePair<BaseMapStarterMenu, GameObject>>();

	internal static void RefreshOpen()
	{
		if (_open != null)
		{
			_open.Refresh();
		}
		RandomWorldPanel.RefreshOpen();
	}

	private static void Postfix(BaseMapStarterMenu __instance, ref PanelList __result)
	{
		if (__result == null)
		{
			return;
		}
		BaseMapStarterMenu menu = __instance;
		Action<DropdownPanelComponent> refresh = (DropdownPanelComponent panel) =>
		{
			Remember(menu, panel.gameObject);
			bool shown = Shown(menu);
			panel.gameObject.SetActive(shown);
			if (!shown)
			{
				return;
			}
			CanvasGroup group = panel.GetComponent<CanvasGroup>() ?? panel.gameObject.AddComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
			LayoutElement layout = panel.GetComponent<LayoutElement>() ?? panel.gameObject.AddComponent<LayoutElement>();
			layout.ignoreLayout = true;
		};
		__result.Insert(Math.Min(3, __result.Count), DropdownPanel.New(refresh, refresh).As(DropdownPanel.Variant.StandardBig));
		__result.Insert(Math.Min(4, __result.Count), SaveSeed(menu));
	}

	// A save of a random world: its seed, a click copies its share code (to make the same world again, or give it to
	// someone: paste it in the random world's panel).
	private static IPanel SaveSeed(BaseMapStarterMenu menu)
	{
		Action<LegacyButtonPanelComponent> reset = (LegacyButtonPanelComponent panel) =>
		{
			panel._button.onClick.RemoveAllListeners();
			panel._button.onClick.AddListener(() =>
			{
				if (SavedWorld(menu, out WorldSettings settings))
				{
					CastleStoryPlus.UI.SystemClipboard.Set(RandomWorld.ShareCode(settings));
					panel._keyLabel.text = "Share code copied";
					MenuSound.PlaySound(menu.SoundPrefix, MenuSound.Suffix.Select);
				}
			});
		};
		Action<LegacyButtonPanelComponent> refresh = (LegacyButtonPanelComponent panel) =>
		{
			if (!SavedWorld(menu, out WorldSettings settings))
			{
				panel.gameObject.SetActive(false);
				return;
			}
			panel.gameObject.SetActive(true);
			panel._keyLabel.text = "Seed (click to copy the share code)";
			panel._keyLabel.color = BaseMenu.Colors.Black;
			panel._label.text = settings.Seed.ToUpperInvariant();
			panel._label.color = BaseMenu.Colors.Black;
			panel._selectionImage.gameObject.SetActive(false);
			panel._button.interactable = true;
		};
		LegacyButtonPanelComponent.BasePanel button = LegacyButtonPanelComponent.Build(reset, refresh).As(LegacyButtonPanelComponent.BasePanel.Variant.LegacyReskin_DblTxt_RightIcon);
		button.IgnoreWidgets();
		return button;
	}

	// The settings a shown save of a random world was made with.
	private static bool SavedWorld(BaseMapStarterMenu menu, out WorldSettings settings)
	{
		settings = default(WorldSettings);
		Asset_Map map = menu.DisplayedMap;
		if (map == null || map.codename == null || !map.codename.StartsWith(RandomWorld.Codename, StringComparison.Ordinal) || RandomWorld.IsRandomWorld(map))
		{
			return false;
		}
		return RandomWorld.TryParseDescription(map.description, out settings);
	}

	private static void Remember(BaseMapStarterMenu menu, GameObject marker)
	{
		for (int i = Markers.Count - 1; i >= 0; i--)
		{
			if (Markers[i].Value == null || Markers[i].Value == marker || Markers[i].Key == menu)
			{
				Markers.RemoveAt(i);
			}
		}
		Markers.Add(new KeyValuePair<BaseMapStarterMenu, GameObject>(menu, marker));
	}

	// Whether the panel shows now, and whether the game is single player (the start choice).
	internal static bool PanelShown(out bool single)
	{
		single = false;
		_open = null;
		foreach (KeyValuePair<BaseMapStarterMenu, GameObject> marker in Markers)
		{
			if (marker.Value != null && marker.Value.activeInHierarchy && Shown(marker.Key))
			{
				_open = marker.Key;
				break;
			}
		}
		if (_open == null)
		{
			return false;
		}
		single = !_open.IsMultiplayer;
		return true;
	}

	// The thumbnail the open menu shows for the random world (before a world was made this session).
	internal static Texture ShownThumbnail()
	{
		return (_open != null && _open.DisplayedMap != null) ? _open.DisplayedMap.thumbnail : null;
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

	internal static void Change(WorldSettings settings)
	{
		RandomWorld.Change(settings);
		if (_open != null)
		{
			_open.Refresh();
		}
	}

	internal static void PlaySelect()
	{
		if (_open != null)
		{
			MenuSound.PlaySound(_open.SoundPrefix, MenuSound.Suffix.Select);
		}
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
