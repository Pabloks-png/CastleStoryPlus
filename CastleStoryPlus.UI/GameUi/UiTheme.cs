using UnityEngine;

namespace CastleStoryPlus.GameUi;

// The in-game menu's newer look (the workshop popup first), kept in the reloadable UI assembly so it can be tuned
// with "Reload UI". The game's own colours (CastleYellow...) stay in LuiStyle.
internal static class UiTheme
{
	// Captions and second lines in a warm grey, a slot's faint fill,
	// the darker block of what is going on, the thin lines between sections and green for "enough".
	public static readonly Color TextMuted = new Color(0.62f, 0.58f, 0.52f, 1f);

	public static readonly Color TextDim = new Color(0.52f, 0.49f, 0.44f, 1f);

	public static readonly Color BgSlot = new Color(1f, 1f, 1f, 0.06f);

	public static readonly Color BgBlock = new Color(0f, 0f, 0f, 0.25f);

	public static readonly Color BgSelectedRow = new Color(1f, 197f / 255f, 10f / 255f, 0.09f);

	public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);

	public static readonly Color Good = new Color(0.54f, 0.82f, 0.35f, 1f);

	// A colour as rich text wants it ("#8ad15a").
	public static string Hex(Color color)
	{
		return "#" + ColorUtility.ToHtmlStringRGB(color);
	}


	// The popup's own background: darker and less see-through than the bars, so the world does not show through
	// the text.
	public static readonly Color BgPopup = new Color(0.075f, 0.07f, 0.06f, 0.95f);

	// The dark text on a yellow background (a chosen option).
	public static readonly Color TextOnYellow = new Color(0.11f, 0.09f, 0.03f, 1f);

	// ---- Rounded corners ----

	// Corner radii in canvas pixels: panels, buttons and rows, slots, and the pills (counters, choices, bars).
	public const int PanelRadius = 10;

	public const int ButtonRadius = 8;

	public const int SlotRadius = 6;

	public const int PillRadius = 11;

	public const int BarRadius = 3;

	private static readonly System.Collections.Generic.Dictionary<int, Sprite> Fills = new System.Collections.Generic.Dictionary<int, Sprite>();

	private static Sprite _outline;

	// Sets the rounded look for the plugin's own widgets too (LuiStyle: buttons, sidebars, tooltips, frames).
	public static void Install()
	{
		LuaUi.LuiStyle.RoundFill = Fill(ButtonRadius);
		LuaUi.LuiStyle.RoundOutline = Outline();
	}

	// A white sliced sprite with rounded corners of that radius, made once.
	public static Sprite Fill(int radius)
	{
		if (!Fills.TryGetValue(radius, out Sprite sprite) || sprite == null)
		{
			sprite = Make(radius, 0f);
			Fills[radius] = sprite;
		}
		return sprite;
	}

	// The highlight frame: a 2 pixel line round the button radius.
	public static Sprite Outline()
	{
		if (_outline == null)
		{
			_outline = Make(ButtonRadius, 2f);
		}
		return _outline;
	}

	// Gives an image a rounded fill and a colour.
	public static void Round(UnityEngine.UI.Image image, Color color, int radius)
	{
		image.sprite = Fill(radius);
		image.type = UnityEngine.UI.Image.Type.Sliced;
		image.color = color;
	}

	// Draws the corners with a smooth edge (the distance to a rounded box, one pixel wide); stroke 0 fills it.
	private static Sprite Make(int radius, float stroke)
	{
		int size = radius * 2 + 4;
		Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
		texture.filterMode = FilterMode.Bilinear;
		texture.wrapMode = TextureWrapMode.Clamp;
		float half = size / 2f;
		Color[] pixels = new Color[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
				float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
				float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
				float alpha = Mathf.Clamp01(0.5f - outside);
				if (stroke > 0f)
				{
					alpha *= Mathf.Clamp01(outside + stroke + 0.5f);
				}
				pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
			}
		}
		texture.SetPixels(pixels);
		texture.Apply(false, true);
		float border = radius + 2f;
		Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
		sprite.name = "CSP round " + radius + ((stroke > 0f) ? " outline" : string.Empty);
		return sprite;
	}
}

// A value with "-" and "+" beside it in the newer look: yellow signs on a faint strip that lightens under the mouse,
// the value white in the middle. step is called with -1 or +1 (with Shift held: -10 or +10, as the game's own).
internal class UiCounter : MonoBehaviour
{
	private UnityEngine.UI.Text _value;

	public static UiCounter Create(Transform parent, float width, System.Action<int> step)
	{
		RectTransform rect = CastleStoryPlus.UI.UiKit.CreateRect("Counter", parent);
		CastleStoryPlus.UI.UiKit.Fixed(rect.gameObject, width, 24f);
		UiCounter counter = rect.gameObject.AddComponent<UiCounter>();
		UnityEngine.UI.HorizontalLayoutGroup layout = rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		layout.spacing = 2f;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = true;
		UnityEngine.UI.Image background = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
		UiTheme.Round(background, UiTheme.BgSlot, UiTheme.PillRadius);
		Look(CastleStoryPlus.UI.UiKit.CreateButton(rect, "-", 24f, () => step(LuaUi.LuiInput.Shift ? -10 : -1)));
		counter._value = LuaUi.LuiWidgets.Label(rect, string.Empty, LuaUi.LuiStyle.FontSize, LuaUi.LuiStyle.White, TextAnchor.MiddleCenter, LuaUi.LuiStyle.Bold);
		CastleStoryPlus.UI.UiKit.Flexible(counter._value.gameObject);
		Look(CastleStoryPlus.UI.UiKit.CreateButton(rect, "+", 24f, () => step(LuaUi.LuiInput.Shift ? 10 : 1)));
		return counter;
	}

	private static void Look(UnityEngine.UI.Button button)
	{
		UnityEngine.UI.Image image = button.GetComponent<UnityEngine.UI.Image>();
		if (image != null)
		{
			image.sprite = UiTheme.Fill(UiTheme.PillRadius);
			image.type = UnityEngine.UI.Image.Type.Sliced;
		}
		UnityEngine.UI.ColorBlock colors = button.colors;
		colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
		colors.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
		colors.pressedColor = new Color(1f, 197f / 255f, 10f / 255f, 0.35f);
		button.colors = colors;
		UnityEngine.UI.Text text = button.GetComponentInChildren<UnityEngine.UI.Text>();
		if (text != null)
		{
			text.color = LuaUi.LuiStyle.CastleYellow;
			text.font = LuaUi.LuiStyle.Bold;
			text.fontSize = 16;
		}
	}

	public void Set(string value)
	{
		value = value ?? string.Empty;
		if (_value.text != value)
		{
			_value.text = value;
		}
	}

	public void SetFontSize(int size)
	{
		_value.fontSize = size;
	}
}
