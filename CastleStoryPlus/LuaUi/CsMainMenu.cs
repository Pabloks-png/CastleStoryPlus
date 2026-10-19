using System.Collections.Generic;
using CastleStoryPlus.Core;
using CastleStoryPlus.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleStoryPlus.LuaUi;

// [LuaUi] CsMainMenu: the title screen's button columns drawn in C#. Unlike the in-game menu the title screen is
// not Lua (the LUI/Menus/Title scripts are unused): SceneMenu holds legacy C# menus, Containers whose items
// (Containee buttons) are laid out in the scene, named "Left: Main", "Left: Play", "Left: Play: Single Player",
// "Left: Options"... So the same split is made on those: the containers, their items, the populators that act on a
// click (ContainerGenericPopulator, the play, multiplayer and options menus, the mod's Continue), the open and
// close of the sub-menus (items' action lists, interpolators) and the layout all stay the game's; each plain
// button item gets a C# button over it that draws the item's look again (its row background, title, subtitle,
// chevron and icon, each where and as the game draws it), and the item's own graphics are made invisible. A
// click and the hover are handed to the item's own button, so sounds, signals and navigation are the game's.
// Items that are more than a button (sliders, toggles, input fields, right-click items) stay drawn by the game,
// as do the column's background, the right-hand panels (map browser, lobby, options content) and dialogs.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
[HarmonyPatch(typeof(Container), "OnEnable")]
internal static class CsMainMenuPatch
{
	private static bool Prepare()
	{
		return LuaUiConfig.CsMainMenu;
	}

	// Container.OnEnable has populated the items by now.
	private static void Postfix(Container __instance)
	{
		CsTitleColumn.Attach(__instance);
	}
}

// One of the title screen's button columns: keeps a C# button on each of its plain button items.
internal class CsTitleColumn : MonoBehaviour
{
	private const string Scene = "SceneMenu";

	private static readonly HashSet<string> Logged = new HashSet<string>();

	private Container _container;

	private readonly Dictionary<Containee, CsTitleButton> _skins = new Dictionary<Containee, CsTitleButton>();

	private readonly HashSet<Containee> _skipped = new HashSet<Containee>();

	public static void Attach(Container container)
	{
		// The menu scene may still be loading (the active scene is then the previous one): the container's own.
		if (container == null || container.gameObject.scene.name != Scene || !IsColumn(container.name) || container.GetComponent<CsTitleColumn>() != null)
		{
			return;
		}
		container.gameObject.AddComponent<CsTitleColumn>()._container = container;
		if (Logged.Add(container.name))
		{
			Plugin.Log.LogInfo("CsMainMenu: the buttons of " + container.name + " are drawn in C#");
		}
	}

	// The left columns of SceneMenu; the key bindings list (right-click to clear, rebinding) stays the game's.
	private static bool IsColumn(string name)
	{
		return name.StartsWith("Left: ") && !name.Contains("Keybindings");
	}

	// Whether an item is only a button, which the C# button can stand in for.
	private static bool IsPlainButton(Containee containee)
	{
		return containee.button != null && containee.altButton == null && containee.slider == null && containee.toggle == null && containee.inputField == null && containee.actualInputField == null && containee.GetComponentInChildren<InputField>(true) == null;
	}

	private void LateUpdate()
	{
		if (_container == null)
		{
			return;
		}
		foreach (Container.Item item in _container.items)
		{
			Containee containee = item.instance;
			if (containee == null || _skipped.Contains(containee))
			{
				continue;
			}
			if (_skins.TryGetValue(containee, out CsTitleButton skin) && skin != null)
			{
				continue;
			}
			if (!IsPlainButton(containee))
			{
				_skipped.Add(containee);
				continue;
			}
			_skins[containee] = CsTitleButton.Create(containee);
		}
	}

	private void OnDestroy()
	{
		_skins.Clear();
		_skipped.Clear();
	}
}

// The C# face of one title screen button: a child of the game's item that draws a copy of each of the item's
// images and texts at the same place, size and scale, with the same sprite, font and colour, read every frame (so
// populators changing a title, subtitle or icon show at once). The item's own graphics are kept invisible (their
// alpha is set every frame, after the button's colour fades). The row background takes the button's colours as
// the game's ColorTint does: normal (white for the chosen entry of a tab column), highlighted under the mouse,
// pressed while held, disabled.
internal class CsTitleButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
	private const string SkinName = "CSP_TitleButton";

	private const float GraphicsSeconds = 1f;

	private class Twin
	{
		public Graphic Source;

		public Graphic Copy;
	}

	private Containee _containee;

	private readonly List<Twin> _twins = new List<Twin>();

	private readonly HashSet<Graphic> _twinned = new HashSet<Graphic>();

	private float _graphicsAt = -100f;

	private bool _hovered;

	private bool _pressed;

	private bool _enabled = true;

	public static CsTitleButton Create(Containee containee)
	{
		// A pooled or cloned item may carry a skin bound to another item: that one goes.
		Transform old = containee.transform.Find(SkinName);
		if (old != null)
		{
			Destroy(old.gameObject);
		}
		RectTransform rect = UiKit.CreateRect(SkinName, containee.transform);
		UiKit.SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
		CsTitleButton view = rect.gameObject.AddComponent<CsTitleButton>();
		view._containee = containee;
		view.Refresh();
		view.Sync();
		return view;
	}

	private void LateUpdate()
	{
		if (_containee == null)
		{
			Destroy(gameObject);
			return;
		}
		// Taken out of the title screen's columns (an object pool): give the item its own look back.
		if (_containee.container == null || _containee.container.GetComponent<CsTitleColumn>() == null)
		{
			Destroy(gameObject);
			return;
		}
		if (transform.GetSiblingIndex() != transform.parent.childCount - 1)
		{
			transform.SetAsLastSibling();
		}
		if (Time.unscaledTime - _graphicsAt >= GraphicsSeconds)
		{
			Refresh();
		}
		Sync();
	}

	// Copies made for graphics the item got since (populators add and swap some), dropped for those gone.
	private void Refresh()
	{
		_graphicsAt = Time.unscaledTime;
		for (int i = _twins.Count - 1; i >= 0; i--)
		{
			if (_twins[i].Source == null || _twins[i].Copy == null)
			{
				if (_twins[i].Copy != null)
				{
					Destroy(_twins[i].Copy.gameObject);
				}
				_twinned.Remove(_twins[i].Source);
				_twins.RemoveAt(i);
			}
		}
		// In the item's order, which is the order the game draws them in.
		foreach (Graphic source in _containee.GetComponentsInChildren<Graphic>(true))
		{
			if (source.transform.IsChildOf(transform) || _twinned.Contains(source))
			{
				continue;
			}
			Graphic copy = MakeCopy(source);
			if (copy != null)
			{
				_twins.Add(new Twin { Source = source, Copy = copy });
				_twinned.Add(source);
			}
		}
	}

	// An image, raw image or text like the source; other kinds of graphic are left to the game (not hidden).
	private Graphic MakeCopy(Graphic source)
	{
		RectTransform rect = UiKit.CreateRect(source.name, transform);
		rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
		Graphic copy;
		Image image = source as Image;
		Text text = source as Text;
		RawImage raw = source as RawImage;
		if (image != null)
		{
			Image twin = rect.gameObject.AddComponent<Image>();
			twin.type = image.type;
			twin.preserveAspect = image.preserveAspect;
			twin.fillCenter = image.fillCenter;
			twin.fillMethod = image.fillMethod;
			twin.fillAmount = image.fillAmount;
			twin.fillOrigin = image.fillOrigin;
			twin.fillClockwise = image.fillClockwise;
			copy = twin;
		}
		else if (text != null)
		{
			Text twin = rect.gameObject.AddComponent<Text>();
			twin.font = text.font;
			twin.fontStyle = text.fontStyle;
			twin.alignment = text.alignment;
			twin.alignByGeometry = text.alignByGeometry;
			twin.lineSpacing = text.lineSpacing;
			twin.supportRichText = text.supportRichText;
			twin.horizontalOverflow = text.horizontalOverflow;
			twin.verticalOverflow = text.verticalOverflow;
			twin.resizeTextForBestFit = text.resizeTextForBestFit;
			twin.resizeTextMinSize = text.resizeTextMinSize;
			twin.resizeTextMaxSize = text.resizeTextMaxSize;
			copy = twin;
		}
		else if (raw != null)
		{
			copy = rect.gameObject.AddComponent<RawImage>();
		}
		else
		{
			Destroy(rect.gameObject);
			return null;
		}
		// The mouse is caught where the game's button catches it: its background (or every image without one).
		Graphic target = _containee.button.targetGraphic;
		copy.raycastTarget = (target != null) ? source == target : image != null;
		foreach (Shadow effect in source.GetComponents<Shadow>())
		{
			Shadow twin = (effect is Outline) ? rect.gameObject.AddComponent<Outline>() : rect.gameObject.AddComponent<Shadow>();
			twin.effectColor = effect.effectColor;
			twin.effectDistance = effect.effectDistance;
			twin.useGraphicAlpha = effect.useGraphicAlpha;
		}
		return copy;
	}

	// Places, contents and colours from the sources; the sources made invisible.
	private void Sync()
	{
		Button button = _containee.button;
		_enabled = button == null || button.IsInteractable();
		Vector3 scale = transform.lossyScale;
		foreach (Twin twin in _twins)
		{
			Graphic source = twin.Source;
			Graphic copy = twin.Copy;
			if (source == null || copy == null)
			{
				continue;
			}
			bool shown = source.isActiveAndEnabled;
			if (copy.gameObject.activeSelf != shown)
			{
				copy.gameObject.SetActive(shown);
			}
			if (!shown)
			{
				continue;
			}
			source.canvasRenderer.SetAlpha(0f);
			RectTransform from = source.rectTransform;
			RectTransform to = copy.rectTransform;
			to.pivot = from.pivot;
			to.sizeDelta = from.rect.size;
			to.position = from.position;
			to.rotation = from.rotation;
			Vector3 lossy = from.lossyScale;
			to.localScale = new Vector3(Ratio(lossy.x, scale.x), Ratio(lossy.y, scale.y), 1f);
			Copy(source, copy);
			copy.color = (button != null && source == button.targetGraphic) ? Tinted(button, source.color) : source.color;
		}
	}

	private static float Ratio(float value, float by)
	{
		return (Mathf.Abs(by) > 0.0001f) ? value / by : 1f;
	}

	private static void Copy(Graphic source, Graphic copy)
	{
		Image image = source as Image;
		if (image != null)
		{
			Image twin = (Image)copy;
			Sprite sprite = image.overrideSprite;
			if (twin.sprite != sprite)
			{
				twin.sprite = sprite;
			}
			if (twin.type != image.type)
			{
				twin.type = image.type;
			}
			return;
		}
		Text text = source as Text;
		if (text != null)
		{
			Text twin = (Text)copy;
			if (twin.text != text.text)
			{
				twin.text = text.text;
			}
			if (twin.fontSize != text.fontSize)
			{
				twin.fontSize = text.fontSize;
			}
			if (twin.font != text.font)
			{
				twin.font = text.font;
			}
			if (twin.alignment != text.alignment)
			{
				twin.alignment = text.alignment;
			}
			return;
		}
		RawImage raw = source as RawImage;
		if (raw != null)
		{
			RawImage twin = (RawImage)copy;
			if (twin.texture != raw.texture)
			{
				twin.texture = raw.texture;
			}
			twin.uvRect = raw.uvRect;
		}
	}

	// The row's colour as the button's ColorTint transition shows it in its present state.
	private Color Tinted(Button button, Color color)
	{
		if (button.transition != Selectable.Transition.ColorTint)
		{
			return color;
		}
		ColorBlock colors = button.colors;
		Color tint;
		if (!_enabled)
		{
			tint = colors.disabledColor;
		}
		else if (_pressed && _hovered)
		{
			tint = colors.pressedColor;
		}
		else if (_hovered)
		{
			tint = colors.highlightedColor;
		}
		else
		{
			tint = colors.normalColor;
		}
		return color * tint * colors.colorMultiplier;
	}

	// Where the item listens: its button, as a click on the game's own drawing would reach it.
	private GameObject Target
	{
		get { return (_containee.button != null) ? _containee.button.gameObject : _containee.gameObject; }
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		_hovered = true;
		// The item's button gets enter and exit itself when it is a parent of this one (hover sound included).
		if (_containee != null && !transform.IsChildOf(Target.transform))
		{
			ExecuteEvents.ExecuteHierarchy(Target, eventData, ExecuteEvents.pointerEnterHandler);
		}
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		_hovered = false;
		_pressed = false;
		if (_containee != null && !transform.IsChildOf(Target.transform))
		{
			ExecuteEvents.ExecuteHierarchy(Target, eventData, ExecuteEvents.pointerExitHandler);
		}
	}

	// Taking the press keeps it from going on to the item's button, so the click comes here and is handed on once.
	public void OnPointerDown(PointerEventData eventData)
	{
		_pressed = eventData.button == PointerEventData.InputButton.Left;
	}

	public void OnPointerUp(PointerEventData eventData)
	{
		_pressed = false;
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		if (_containee == null || !_enabled || eventData.button != PointerEventData.InputButton.Left)
		{
			return;
		}
		ExecuteEvents.ExecuteHierarchy(Target, eventData, ExecuteEvents.pointerClickHandler);
	}

	// The item's own graphics visible again (the skin goes with an item leaving the title screen).
	private void OnDestroy()
	{
		foreach (Twin twin in _twins)
		{
			if (twin.Source != null)
			{
				twin.Source.canvasRenderer.SetAlpha(1f);
			}
		}
		_twins.Clear();
		_twinned.Clear();
		_containee = null;
	}
}
