using System;
using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Semantique;
using Brix.Input;
using Brix.Lua;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleStoryPlus.UI;

// The mouse over a resource lying loose on the ground (a log, a stone, planks, a kit...) shows a small label at the
// cursor with its icon and name, and how many when it is a pile. The game's pickers already ray-cast the world under
// the mouse every frame (Picker.OnAction); the thing they hit is kept and named here. Not over the menus.
[Feature(Features.ItemHint, Features.ItemHintInfo)]
internal sealed class ItemHint : MonoBehaviour
{
	private static GameObject _hovered;

	private static int _hoveredFrame = -1;

	private GameObject _shownFor;

	private RectTransform _canvas;

	private RectTransform _box;

	private Image _icon;

	private Text _text;

	private static void Enable()
	{
		Plugin.Root.AddComponent<ItemHint>();
	}

	internal static void Hovered(GameObject go)
	{
		if (go != null)
		{
			_hovered = go;
			_hoveredFrame = Time.frameCount;
		}
		else if (_hoveredFrame != Time.frameCount)
		{
			_hovered = null;
		}
	}

	private void LateUpdate()
	{
		GameObject item = (_hoveredFrame >= Time.frameCount - 1) ? Item(_hovered) : null;
		if (item != null && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
		{
			item = null;
		}
		if (item == null)
		{
			if (_box != null && _box.gameObject.activeSelf)
			{
				_box.gameObject.SetActive(false);
			}
			_shownFor = null;
			return;
		}
		EnsureBox();
		if (item != _shownFor)
		{
			_shownFor = item;
			Describe(item);
		}
		_box.gameObject.SetActive(true);
		Vector3 mouse = Input.mousePosition;
		_box.position = new Vector3(mouse.x + 18f, mouse.y - 18f, 0f);
	}

	// The loose item the hit belongs to, or null.
	private static GameObject Item(GameObject hit)
	{
		if (hit.IsNullOrReleased() || !hit.activeInHierarchy)
		{
			return null;
		}
		IDescriptor descriptor = hit.GetComponentInParent<IDescriptor>();
		Component component = descriptor as Component;
		if (component == null || component.GetComponent<Labor>() != null || component.GetComponent<Recepteur>() != null)
		{
			return null;
		}
		Transform parent = component.transform.parent;
		if (parent != null && (parent.GetComponentInParent<Recepteur>() != null || parent.GetComponentInParent<Labor>() != null))
		{
			return null;
		}
		return (FirstResource(descriptor.Description) != null) ? component.gameObject : null;
	}

	private static Ressource FirstResource(Description description)
	{
		if (description == null)
		{
			return null;
		}
		foreach (KeyValuePair<Type, Adjectif> pair in description.DicoAdjectif)
		{
			if (pair.Value is Ressource resource && resource.quantifiable != null && resource.quantifiable.valeur > 0)
			{
				return resource;
			}
		}
		return null;
	}

	private void Describe(GameObject item)
	{
		Ressource resource = FirstResource(item.GetComponent<IDescriptor>().Description);
		string name = null;
		Sprite icon = null;
		GameUi.CraftingCatalog.EnsureMapped();
		Dictionary<AdjectiveKey, LuaCrafting.LuaResource> toLua = LuaCrafting._adjectiveToLuaResource;
		if (resource != null && toLua != null && toLua.TryGetValue(resource.GetKey(), out LuaCrafting.LuaResource lua))
		{
			name = GameUi.CraftingCatalog.ResourceName(lua);
			icon = GameUi.CraftingCatalog.ResourceIcon(lua);
		}
		if (string.IsNullOrEmpty(name))
		{
			name = item.name;
			int bracket = name.IndexOf(" (", StringComparison.Ordinal);
			if (bracket > 0)
			{
				name = name.Substring(0, bracket);
			}
		}
		int count = (resource != null) ? resource.quantifiable.valeur : 1;
		_text.text = (count > 1) ? name + " x" + count : name;
		_icon.sprite = icon;
		_icon.gameObject.SetActive(icon != null);
		float width = 16f + ((icon != null) ? 24f : 0f) + _text.preferredWidth;
		_box.sizeDelta = new Vector2(width, 28f);
		_text.rectTransform.offsetMin = new Vector2((icon != null) ? 32f : 8f, 0f);
	}

	private void EnsureBox()
	{
		if (_box != null)
		{
			return;
		}
		_canvas = UiKit.CreateCanvas("CastleStoryPlusItemHint", transform, 960).GetComponent<RectTransform>();
		_canvas.GetComponent<GraphicRaycaster>().enabled = false;
		_box = UiKit.CreateRect("Hint", _canvas);
		_box.pivot = new Vector2(0f, 1f);
		Image background = _box.gameObject.AddComponent<Image>();
		background.color = UiKit.Background;
		background.raycastTarget = false;
		RectTransform iconRect = UiKit.CreateRect("Icon", _box);
		iconRect.anchorMin = new Vector2(0f, 0.5f);
		iconRect.anchorMax = new Vector2(0f, 0.5f);
		iconRect.pivot = new Vector2(0f, 0.5f);
		iconRect.anchoredPosition = new Vector2(6f, 0f);
		iconRect.sizeDelta = new Vector2(22f, 22f);
		_icon = iconRect.gameObject.AddComponent<Image>();
		_icon.preserveAspect = true;
		_icon.raycastTarget = false;
		_text = UiKit.CreateText(_box, string.Empty, 14, UiKit.Yellow, TextAnchor.MiddleLeft);
		_text.rectTransform.anchorMin = Vector2.zero;
		_text.rectTransform.anchorMax = Vector2.one;
		_text.rectTransform.offsetMin = new Vector2(32f, 0f);
		_text.rectTransform.offsetMax = new Vector2(-8f, 0f);
		_box.gameObject.SetActive(false);
	}
}

// What the pickers hit under the mouse (not the terrain).
[Feature(Features.ItemHint, Features.ItemHintInfo)]
[HarmonyPatch(typeof(Picker), nameof(Picker.OnAction))]
internal static class ItemHintHoverPatch
{
	private static void Postfix(Picker __instance)
	{
		VoxelRaycastHit hit = __instance.RaycastHit;
		GameObject go = (hit != null && hit.HitGameObject != null && !hit.HasHitTerrain()) ? hit.HitGameObject : null;
		ItemHint.Hovered(go);
	}
}
