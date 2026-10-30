using System;
using System.Collections.Generic;
using System.Reflection;
using Brix.Assets;
using Brix.Engine;
using Brix.External.Signals;
using Brix.UI.Builder.Menu;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Menus;

// Title screen: clones the Play button into a CONTINUE button and a LOAD GAME button right below it. The title
// buttons are defined in the scene, so the items are added at runtime.
[Feature(Features.ContinueButton, Features.ContinueButtonInfo)]
[HarmonyPatch(typeof(ContainerGenericPopulator), nameof(ContainerGenericPopulator.OnEnable))]
internal static class ContinueButtonPatch
{
	private const string Title = "&&CONTINUE";

	private const string LoadTitle = "&&LOAD GAME";

	private static void Postfix(ContainerGenericPopulator __instance)
	{
		Container container = __instance.container;
		// The menu scene is still loading here, so the active scene is the previous one: use the button's own scene.
		if (container == null || __instance.gameObject.scene.name != "SceneMenu" || container.GetItem(MenuOperations.ContinueGame) != null)
		{
			return;
		}
		int playIdx = container.items.FindIndex((Container.Item i) => i.titleText != null && i.titleText.Equals("##mainmenu_play", StringComparison.OrdinalIgnoreCase));
		if (playIdx < 0)
		{
			if (container.GetItem(LegacyBaseMenu.Operation.QuitApplication) != null)
			{
				Plugin.Log.LogWarning("Continue: Play button not found in " + __instance.name + ": " + string.Join(", ", container.items.ConvertAll((Container.Item i) => i.titleText).ToArray()));
			}
			return;
		}
		Container.Item play = container.items[playIdx];
		if (!AddBelow(container, play, playIdx + 1, Title, "CONTINUE", "Continue", MenuOperations.ContinueGame) || !AddBelow(container, play, playIdx + 2, LoadTitle, "LOAD GAME", "Load game", MenuOperations.OpenLoadGame))
		{
			return;
		}
		Plugin.Log.LogInfo("Continue and Load game buttons added to " + __instance.name);
		AutoContinue.Start();
		if (play.isDynamic && container.isActiveAndEnabled)
		{
			container.Populate(isChain: false);
		}
	}

	// A copy of Play at the index, with its own title and operation.
	private static bool AddBelow(Container container, Container.Item play, int index, string titleText, string shown, string name, LegacyBaseMenu.Operation operation)
	{
		// A copy of every setting of Play (state, group, layout, contexts...): an item left in the default Closed
		// state is hidden by the container.
		Container.Item item = new Container.Item();
		foreach (FieldInfo field in typeof(Container.Item).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			field.SetValue(item, field.GetValue(play));
		}
		item.instance = null;
		item.titleText = titleText;
		item.localizeTitle = string.Empty;
		item.actionItems = new List<Container.Item.ActionItem>();
		item.isDefaultInGroup = false;
		item.doClick = false;
		item.containerPopulatorOperation = operation;
		if (!play.isDynamic)
		{
			if (play.instance == null)
			{
				return false;
			}
			GameObject clone = UnityEngine.Object.Instantiate(play.instance.gameObject, play.instance.transform.parent, worldPositionStays: false);
			clone.name = name;
			clone.transform.SetSiblingIndex(play.instance.transform.GetSiblingIndex() + (index - container.items.IndexOf(play)));
			item.instance = clone.GetComponent<Containee>();
			item.instance.container = container;
			if (item.instance.title != null)
			{
				item.instance.title.text = shown;
			}
		}
		container.items.Insert(index, item);
		return true;
	}
}

// Loads the newest single-player save, like picking it in Load Game and pressing Play.
[Feature(Features.ContinueButton, Features.ContinueButtonInfo)]
[HarmonyPatch(typeof(Container), nameof(Container.ItemClick))]
internal static class ContinueClickPatch
{
	private static bool Prefix(Container __instance, int itemIdx)
	{
		if (itemIdx < 0 || itemIdx >= __instance.items.Count)
		{
			return true;
		}
		LegacyBaseMenu.Operation operation = __instance.items[itemIdx].containerPopulatorOperation;
		if (operation == MenuOperations.OpenLoadGame)
		{
			LoadGameClicker.Open(__instance);
			return false;
		}
		if (operation != MenuOperations.ContinueGame)
		{
			return true;
		}
		ContinueSave(null);
		return false;
	}

	// Loads the newest single-player save, or with a name the newest whose folder name holds it.
	internal static void ContinueSave(string name)
	{
		Asset_Map newest = null;
		foreach (Asset_Map save in AssetDispensary.saves.SendQuery(AssetQueryParameters.AllSPSources | AssetQueryParameters.RefreshCache, AssetPlayableContext.Singleplayer))
		{
			if (save == null || !save.IsCurrentVersion())
			{
				continue;
			}
			if (!string.IsNullOrEmpty(name) && (save.customName == null || save.customName.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0))
			{
				continue;
			}
			if (newest == null || save.dateTimeCreated > newest.dateTimeCreated)
			{
				newest = save;
			}
		}
		if (!string.IsNullOrEmpty(name))
		{
			Plugin.Log.LogInfo("Continue: save matching \"" + name + "\": " + (newest != null ? newest.customName : "none"));
		}
		if (newest != null)
		{
			BaseMapLibraryMenu.SetSelectedMap(newest, AssetPlayableContext.Singleplayer, BaseAssetMenu.MenuGameContext.Normal, BaseAssetMenu.MenuTypeContext.Existing);
		}
		if (newest == null || GameParam.SelectedGamemode == null)
		{
			BaseMapLibraryMenu.EmptySelected();
			GameSignals.Invoke(GameSignals.OnDisplayGenericDialog, new OkDialogContainerPopulator.GenericDialog("CONTINUE", "No save game found.", new OkDialogContainerPopulator.GenericDialog.Btn[1]
			{
				new OkDialogContainerPopulator.GenericDialog.Btn("&&OK", () =>
				{
					GameSignals.Invoke(GameSignals.OnCloseDialog);
				}, string.Empty)
			}));
			return;
		}
		BaseMapLibraryMenu.SetSelectedAsLoaded();
		GameParam.StartGame(() =>
		{
			BaseMapLibraryMenu.EmptySelected();
		});
		MenuSound.PlaySound(MenuSound.Prefix.Title, MenuSound.Suffix.GameStart);
	}
}

// LOAD GAME on the title screen: the game opens its load game screen through its menus only, so the buttons a player
// would press are pressed in turn, a frame or more apart as each sub-menu opens: Play, then Single Player, then Load
// Game.
internal class LoadGameClicker : MonoBehaviour
{
	private const float TimeoutSeconds = 3f;

	private static LoadGameClicker _instance;

	private Container _main;

	private int _step;

	private float _until;

	internal static void Open(Container main)
	{
		if (_instance == null)
		{
			_instance = Plugin.Root.AddComponent<LoadGameClicker>();
		}
		_instance._main = main;
		_instance._step = 0;
		_instance._until = Time.unscaledTime + TimeoutSeconds;
		_instance.enabled = true;
	}

	private void Update()
	{
		if (_main == null || Time.unscaledTime > _until)
		{
			if (_main != null)
			{
				Plugin.Log.LogWarning("Load game: the load game screen was not reached (step " + _step + ")");
			}
			enabled = false;
			return;
		}
		switch (_step)
		{
			case 0:
				if (Click(_main, (Container.Item i) => i.titleText != null && i.titleText.Equals("##mainmenu_play", StringComparison.OrdinalIgnoreCase)))
				{
					_step++;
				}
				break;
			case 1:
				if (ClickAnywhere(LegacyBaseMenu.Operation.SinglePlayer))
				{
					_step++;
				}
				break;
			default:
				if (ClickAnywhere(LegacyBaseMenu.Operation.LoadGame))
				{
					enabled = false;
				}
				break;
		}
	}

	// The item with the operation in any open container of the title screen.
	private static bool ClickAnywhere(LegacyBaseMenu.Operation operation)
	{
		foreach (Container container in FindObjectsOfType<Container>())
		{
			if (container.isActiveAndEnabled && Click(container, (Container.Item i) => i.containerPopulatorOperation == operation))
			{
				return true;
			}
		}
		return false;
	}

	private static bool Click(Container container, Predicate<Container.Item> match)
	{
		int index = container.items.FindIndex(match);
		if (index < 0)
		{
			return false;
		}
		Container.Item item = container.items[index];
		if (item.instance != null && item.instance.button != null)
		{
			if (!item.instance.isActiveAndEnabled)
			{
				return false;
			}
			item.instance.button.onClick.Invoke();
		}
		else
		{
			container.ItemClick(index);
		}
		return true;
	}
}
