using System;
using System.Collections.Generic;
using Brix.UI.Builder.Menu;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleStoryPlus.LuaUi;

// One button of a C# dialog: a bold title and a smaller line under it, as the game's modal buttons ("START" over
// "Start the game"), and what a click does.
internal class ModalButton
{
	public string Title;

	public string Info;

	public Action OnClick;

	// Left open after a click: the click's own code closes it (the game's Lua dialogs close through their hook).
	public bool KeepOpen;
}

// What a C# dialog shows. Only Title is needed; texts may be "##keys" and the body rich text.
internal class ModalSpec
{
	public string Title;

	public string Subtitle;

	public string Body;

	// A picture above the title (the gamemode's intro image).
	public Texture Header;

	public readonly List<ModalButton> Buttons = new List<ModalButton>();

	// The button Enter presses; -1 for none.
	public int DefaultButton;

	// What Escape does; null leaves Escape alone.
	public Action Cancel;

	// An input field is shown when InputHint is set; its text is CsModal.InputText while the dialog is open.
	public string InputHint;

	public string InputText;

	public float Width = 512f;
}

// The C# dialogs: one at a time on LuiCanvas's dialog layer, over a dimmed backdrop that keeps clicks off the menus
// and the world below. Usable from any scene (LuiCanvas is made on first use, also outside a game). A new dialog
// replaces the one open; the Lua dialogs of the gamemodes come through MenuModalLink.
internal static class CsModal
{
	private static ModalView _current;

	// Whoever opened the dialog open now (MenuModalLink: the gamemode's script), so only they close it.
	private static object _owner;

	public static bool IsOpen
	{
		get { return _current != null; }
	}

	// The input field's text, or null when the dialog has none.
	public static string InputText
	{
		get { return (_current != null) ? _current.InputText : null; }
	}

	public static void Show(ModalSpec spec)
	{
		Show(spec, null);
	}

	public static void Show(ModalSpec spec, object owner)
	{
		Close();
		GameObject go = new GameObject("CSP_Modal", typeof(RectTransform));
		go.transform.SetParent(LuiCanvas.Instance.Dialogs, false);
		_current = go.AddComponent<ModalView>();
		_owner = owner;
		_current.Build(spec);
	}

	public static void Close()
	{
		if (_current == null)
		{
			return;
		}
		ModalView view = _current;
		_current = null;
		_owner = null;
		if (view != null)
		{
			UnityEngine.Object.Destroy(view.gameObject);
		}
	}

	// Closes the dialog only when it is the owner's.
	public static void Close(object owner)
	{
		if (_current != null && Equals(_owner, owner))
		{
			Close();
		}
	}

	public static bool OwnedBy(object owner)
	{
		return _current != null && Equals(_owner, owner);
	}

	// What the open dialog shows, or null (DevTools: GET /api/dialog).
	internal static ModalSpec CurrentSpec
	{
		get { return (_current != null) ? _current.Spec : null; }
	}

	// Presses the open dialog's button as a click does; false when no dialog is open or it has no such button.
	internal static bool Press(int index)
	{
		if (_current == null || _current.Spec == null || index < 0 || index >= _current.Spec.Buttons.Count)
		{
			return false;
		}
		Press(_current, _current.Spec.Buttons[index]);
		return true;
	}

	// A button pressed: closed first unless it stays open, so its code may open the next dialog.
	internal static void Press(ModalView view, ModalButton button)
	{
		if (view != _current || button == null)
		{
			return;
		}
		if (!button.KeepOpen)
		{
			Close();
		}
		Invoke(button.OnClick, "dialog button");
	}

	internal static void Cancel(ModalView view, Action cancel)
	{
		if (view != _current || cancel == null)
		{
			return;
		}
		Close();
		Invoke(cancel, "dialog cancel");
	}

	private static void Invoke(Action action, string where)
	{
		if (action == null)
		{
			return;
		}
		try
		{
			action();
		}
		catch (Exception ex)
		{
			LuaBridge.LogOnce(where, ex.ToString());
		}
	}
}

// The dialog itself: backdrop, framed window, header image, title, subtitle, body, input field and buttons.
internal class ModalView : MonoBehaviour
{
	private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);

	private const float HeaderMaxHeight = 220f;

	private const float ButtonHeight = 52f;

	private ModalSpec _spec;

	private readonly List<ModalButton> _buttons = new List<ModalButton>();

	private InputField _input;

	private bool _typing;

	public string InputText
	{
		get { return (_input != null) ? _input.text : null; }
	}

	public ModalSpec Spec
	{
		get { return _spec; }
	}

	public void Build(ModalSpec spec)
	{
		_spec = spec;
		RectTransform root = (RectTransform)transform;
		UiKit.SetRect(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		// The backdrop catches the mouse: nothing below the dialog is clicked, and the game's picking sees UI.
		Image backdrop = gameObject.AddComponent<Image>();
		backdrop.color = Backdrop;
		backdrop.raycastTarget = true;

		RectTransform window = LuiWidgets.Window(root, null, spec.Width);
		UiKit.SetRect(window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(spec.Width, 0f));
		window.pivot = new Vector2(0.5f, 0.5f);
		VerticalLayoutGroup layout = window.GetComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(16, 16, 14, 16);
		layout.spacing = 8f;
		LuiWidgets.Frame(window, LuiStyle.Highlight, 1f).SetActive(true);
		float inner = spec.Width - 32f;

		if (spec.Header != null && spec.Header.width > 0)
		{
			RectTransform header = UiKit.CreateRect("Header", window);
			RawImage image = header.gameObject.AddComponent<RawImage>();
			image.texture = spec.Header;
			image.raycastTarget = false;
			float height = Mathf.Min(HeaderMaxHeight, inner * spec.Header.height / spec.Header.width);
			LayoutElement element = header.gameObject.AddComponent<LayoutElement>();
			element.minHeight = height;
			element.preferredHeight = height;
		}

		Text title = WrappedLabel(window, spec.Title, 26, LuiStyle.CastleYellow, TextAnchor.MiddleCenter, LuiStyle.Title);
		title.name = "Title";
		if (!string.IsNullOrEmpty(spec.Subtitle))
		{
			WrappedLabel(window, spec.Subtitle, 12, LuiStyle.Gray, TextAnchor.MiddleCenter, LuiStyle.Bold).name = "Subtitle";
		}
		if (!string.IsNullOrEmpty(spec.Body))
		{
			LuiWidgets.Divider(window, false);
			Text body = WrappedLabel(window, spec.Body, LuiStyle.FontSize, LuiStyle.White, TextAnchor.UpperLeft, null);
			body.name = "Body";
			body.supportRichText = true;
			body.lineSpacing = 1.1f;
		}
		if (spec.InputHint != null)
		{
			BuildInput(window, inner);
		}
		BuildButtons(window);
		LayoutRebuilder.ForceRebuildLayoutImmediate(window);
		if (_input != null)
		{
			_input.Select();
			_input.ActivateInputField();
		}
	}

	private static Text WrappedLabel(Transform parent, string text, int size, Color color, TextAnchor alignment, Font font)
	{
		Text label = LuiWidgets.Label(parent, LuaBridge.Localized(text ?? string.Empty), size, color, alignment, font);
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		return label;
	}

	private void BuildInput(Transform parent, float width)
	{
		_input = UiKit.CreateInputField(parent, LuaBridge.Localized(_spec.InputHint), width);
		_input.text = _spec.InputText ?? string.Empty;
		// While typing the game's hotkeys are off, as for its own text fields.
		LuaInputFieldFocus focus = _input.gameObject.AddComponent<LuaInputFieldFocus>();
		focus.Changed = SetTyping;
	}

	private void BuildButtons(Transform parent)
	{
		RectTransform row = UiKit.CreateRect("Buttons", parent);
		HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.spacing = 8f;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = true;
		LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
		element.minHeight = ButtonHeight;
		element.preferredHeight = ButtonHeight;
		for (int i = 0; i < _spec.Buttons.Count; i++)
		{
			ModalButton button = _spec.Buttons[i];
			if (button == null)
			{
				continue;
			}
			_buttons.Add(button);
			RectTransform rect = UiKit.CreateRect("Button", row);
			ModalButtonView view = rect.gameObject.AddComponent<ModalButtonView>();
			view.Build(button, i == _spec.DefaultButton);
			view.OnClick = () => CsModal.Press(this, button);
		}
		if (_buttons.Count == 0)
		{
			row.gameObject.SetActive(false);
		}
	}

	private void Update()
	{
		if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && _spec.DefaultButton >= 0 && _spec.DefaultButton < _spec.Buttons.Count)
		{
			CsModal.Press(this, _spec.Buttons[_spec.DefaultButton]);
			return;
		}
		if (_spec.Cancel != null && Input.GetKeyDown(KeyCode.Escape))
		{
			CsModal.Cancel(this, _spec.Cancel);
		}
	}

	private void SetTyping(bool typing)
	{
		if (_typing == typing)
		{
			return;
		}
		_typing = typing;
		if (typing)
		{
			LuaMenuUtility.SetAllKeyBindingsTo(false);
			if (Brix.Input.InputModeController.Instance != null)
			{
				Brix.Input.InputModeController.Instance.ChangeInputToMenu();
			}
		}
		else
		{
			LuaMenuUtility.ResetAllKeyBindings();
			if (Brix.Input.InputModeController.Instance != null)
			{
				Brix.Input.InputModeController.Instance.ChangeInputToGame();
			}
		}
	}

	private void OnDisable()
	{
		// Closed while typing: the keys must come back.
		SetTyping(false);
	}
}

// A dialog button: dark, lighter under the mouse, the default one (Enter) framed in yellow.
internal class ModalButtonView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
	private Image _background;

	private GameObject _hoverFrame;

	public Action OnClick;

	public void Build(ModalButton button, bool isDefault)
	{
		_background = gameObject.AddComponent<Image>();
		LuiStyle.Paint(_background, LuiStyle.ButtonSprite, LuiStyle.BgDefault);
		VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(10, 10, 6, 6);
		layout.spacing = 1f;
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		Text title = LuiWidgets.Label(transform, LuaBridge.Localized(button.Title ?? string.Empty), LuiStyle.TitleFontSize, LuiStyle.CastleYellow, TextAnchor.MiddleCenter, LuiStyle.Bold);
		title.horizontalOverflow = HorizontalWrapMode.Wrap;
		if (!string.IsNullOrEmpty(button.Info))
		{
			Text info = LuiWidgets.Label(transform, LuaBridge.Localized(button.Info), LuiStyle.SmallFontSize, LuiStyle.White, TextAnchor.MiddleCenter, null);
			info.horizontalOverflow = HorizontalWrapMode.Wrap;
		}
		if (isDefault)
		{
			LuiWidgets.Frame(transform, LuiStyle.Highlight, 2f).SetActive(true);
		}
		_hoverFrame = LuiWidgets.Frame(transform, LuiStyle.CastleYellowLight, 1f);
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		_background.color = LuiStyle.Hovered(LuiStyle.BgDefault);
		_hoverFrame.SetActive(true);
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		ShowNormal();
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left || OnClick == null)
		{
			return;
		}
		OnClick();
	}

	private void OnDisable()
	{
		ShowNormal();
	}

	private void ShowNormal()
	{
		if (_background != null)
		{
			_background.color = LuiStyle.BgDefault;
		}
		if (_hoverFrame != null)
		{
			_hoverFrame.SetActive(false);
		}
	}
}
