using System.Collections.Generic;
using System.Reflection;
using Brix.UI.Builder.Menu;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.UI;

// "Enable debug mode" checkbox on the Gameplay page of the game's own Options (title screen and in game, both the
// legacy C# options panel). The page is a Container of Containee items laid out in the scene, its checkboxes
// driven by LegacyGeneralOptionsMenu on a click; one of those items is cloned (look, layout, sounds, click wiring)
// and added to the container as an item of its own, whose click switches [Debug] DebugMode at once (saved by the
// config, not by the options' Apply).
[Feature]
[HarmonyPatch(typeof(Container), "OnEnable")]
internal static class DebugModeOptionPatch
{
	// Not one of the game's operations, so the game's populators leave the item alone.
	internal const LegacyBaseMenu.Operation Operation = (LegacyBaseMenu.Operation)7301;

	// Lower case "debug": Container.Activate hides items whose title contains "Debug".
	private const string Title = "&&Enable debug mode";

	private const string Subtitle = "&&F7 performance, F8 debug menu, F9 system log";

	private static readonly HashSet<string> Logged = new HashSet<string>();

	// Container.OnEnable has populated the items by now (each time the page is shown).
	private static void Postfix(Container __instance)
	{
		if (__instance == null)
		{
			return;
		}
		// Already added: show the current value (a dynamic page has just built the item again).
		Container.Item item = __instance.GetItem(Operation);
		if (item != null && item.instance != null)
		{
			Refresh(item);
			return;
		}
		Container.Item source = FindCheckbox(__instance);
		if (source == null || !IsGameplayPage(__instance))
		{
			return;
		}
		if (item == null)
		{
			item = CopyItem(source);
			__instance.items.Add(item);
		}
		item.instance = Clone(__instance, source);
		Refresh(item);
		if (Logged.Add(__instance.gameObject.scene.name + "/" + __instance.name))
		{
			Plugin.Log.LogInfo("DebugModeOption: \"Enable debug mode\" added to " + __instance.name + " (" + __instance.gameObject.scene.name + ")");
		}
	}

	// A checkbox item of the page to copy: the game's gameplay switches show a checkmark image.
	private static Container.Item FindCheckbox(Container container)
	{
		foreach (Container.Item item in container.items)
		{
			if (item.instance != null && item.instance.checkmarkImage != null && item.containerPopulatorOperation != LegacyBaseMenu.Operation.ToggleScreenNudge && item.containerPopulatorOperation != Operation)
			{
				return item;
			}
		}
		return null;
	}

	// The page the options' Gameplay tab opens (the tab item's action targets); without that link, the page with
	// the game's own "show help at game start" switch.
	private static bool IsGameplayPage(Container container)
	{
		bool linked = false;
		foreach (LegacyOptionCategoriesMenu menu in Resources.FindObjectsOfTypeAll<LegacyOptionCategoriesMenu>())
		{
			if (menu == null || !menu.gameObject.scene.IsValid() || menu.TabsContainer == null)
			{
				continue;
			}
			foreach (Container.Item tab in menu.TabsContainer.items)
			{
				foreach (Container.Item.ActionItem action in tab.actionItems)
				{
					if (action == null || action.target == null || action.action == Container.Item.ActionItem.Action.Close)
					{
						continue;
					}
					linked = true;
					if (tab.containerPopulatorOperation == LegacyBaseMenu.Operation.OptionsGameplay && container.transform.IsChildOf(action.target.transform))
					{
						return true;
					}
				}
			}
		}
		return !linked && container.GetItem(LegacyBaseMenu.Operation.ShowHelpAtGameStart) != null;
	}

	// The source item's settings (contexts, layout, colours, dynamic or not), with its own title and no actions.
	private static Container.Item CopyItem(Container.Item source)
	{
		Container.Item item = new Container.Item();
		foreach (FieldInfo field in typeof(Container.Item).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			field.SetValue(item, field.GetValue(source));
		}
		item.containerPopulatorOperation = Operation;
		item.titleText = Title;
		item.subtitleText = string.IsNullOrEmpty(source.subtitleText) ? string.Empty : Subtitle;
		item.subsubtitleText = string.Empty;
		item.subsubsubtitleText = string.Empty;
		item.actionItems = new List<Container.Item.ActionItem>();
		item.plugUpdateAction = null;
		item.isDefaultInGroup = false;
		item.toggleFlag = DebugMode.On;
		return item;
	}

	// A copy of the source item's object (the button's click goes to the copy's own Containee), after the page's
	// last item. A dynamic page builds it again from the item's original on its next populate.
	private static Containee Clone(Container container, Container.Item source)
	{
		Transform parent = source.instance.transform.parent;
		GameObject go = Object.Instantiate(source.instance.gameObject, parent, false);
		go.name = "CSP_DebugModeOption";
		go.hideFlags = HideFlags.DontSave;
		int last = source.instance.transform.GetSiblingIndex();
		foreach (Container.Item item in container.items)
		{
			if (item.instance != null && item.instance.transform.parent == parent)
			{
				last = Mathf.Max(last, item.instance.transform.GetSiblingIndex());
			}
		}
		go.transform.SetSiblingIndex(last + 1);
		Containee containee = go.GetComponent<Containee>();
		containee.container = container;
		go.SetActive(source.instance.gameObject.activeSelf);
		return containee;
	}

	// The item's look for the current value, the way LegacyGeneralOptionsMenu shows the game's switches.
	internal static void Refresh(Container.Item item)
	{
		if (item == null || item.instance == null)
		{
			return;
		}
		Containee containee = item.instance;
		if (containee.GetComponent<DebugModeOptionView>() == null)
		{
			containee.gameObject.AddComponent<DebugModeOptionView>().Item = item;
		}
		bool on = DebugMode.On;
		item.toggleFlag = on;
		if (containee.title != null)
		{
			string title = I2Helper.TryGet(item.titleText);
			containee.title.text = item.IsTitleCaps ? title.ToUpper() : title;
		}
		if (containee.subtitle != null)
		{
			string subtitle = I2Helper.TryGet(item.subtitleText);
			containee.subtitle.text = item.IsSubtitleCaps ? subtitle.ToUpper() : subtitle;
		}
		if (containee.toggleText != null)
		{
			containee.toggleText.text = I2Helper.TryGet(on ? Container.Item.StrEnabled : Container.Item.StrDisabled).ToUpper();
		}
		if (containee.checkmarkImage != null)
		{
			containee.checkmarkImage.enabled = on;
		}
		if (containee.toggle != null && containee.toggle.isOn != on)
		{
			containee.toggle.isOn = on;
		}
	}

	internal static bool IsOurs(Container container, int idx)
	{
		Container.Item item = container.GetItem(idx);
		return item != null && item.containerPopulatorOperation == Operation;
	}
}

// A click on the checkbox switches debug mode (the game's own handlers ignore the item's operation).
[Feature]
[HarmonyPatch(typeof(Container), nameof(Container.ItemClick))]
internal static class DebugModeOptionClickPatch
{
	private static void Postfix(Container __instance, int itemIdx)
	{
		if (!DebugModeOptionPatch.IsOurs(__instance, itemIdx))
		{
			return;
		}
		DebugMode.Entry.Value = !DebugMode.On;
		DebugModeOptionPatch.Refresh(__instance.GetItem(itemIdx));
	}
}

// An item with a Toggle and no button reports its new value instead of a click.
[Feature]
[HarmonyPatch(typeof(Container), nameof(Container.ItemToggle))]
internal static class DebugModeOptionTogglePatch
{
	private static void Postfix(Container __instance, int idx, bool flag)
	{
		if (!DebugModeOptionPatch.IsOurs(__instance, idx))
		{
			return;
		}
		Container.Item item = __instance.GetItem(idx);
		if (item.instance != null && item.instance.button != null)
		{
			return;
		}
		if (DebugMode.On != flag)
		{
			DebugMode.Entry.Value = flag;
		}
		DebugModeOptionPatch.Refresh(item);
	}
}

// Keeps the checkbox in step when debug mode is switched elsewhere (Castle Story Plus settings).
internal class DebugModeOptionView : MonoBehaviour
{
	public Container.Item Item;

	private void LateUpdate()
	{
		if (Item != null && Item.toggleFlag != DebugMode.On)
		{
			DebugModeOptionPatch.Refresh(Item);
		}
	}
}
