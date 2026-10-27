using System;
using System.Collections.Generic;
using System.Globalization;
using CastleStoryPlus.LuaUi;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.WorldGen;

// The random world's own panel in the middle of the new game screen, in the menu's yellow: the world's picture large
// on the left (darkened with "MAKING THE WORLD..." while it is made), the choices on the right (each a caption and
// "<  VALUE  >" to step through it), and the seed under them: a new random one, one typed or pasted, and COPY / PASTE
// of a share code (every setting and the seed, RandomWorld.ShareCode) to give a world to someone else. It shows
// while the random world is the map on the new game screen; the screen's own sidebar keeps the mode, preset and
// the play button.
internal class RandomWorldPanel : MonoBehaviour
{
	private const float PictureWidth = 640f;

	private const float PictureHeight = 360f;

	private const float ChoicesWidth = 360f;

	private const float Pad = 16f;

	private static readonly Color Ink = new Color(0.11f, 0.09f, 0.03f, 1f);

	private static readonly Color InkSoft = new Color(0.11f, 0.09f, 0.03f, 0.8f);

	private static readonly Color Strip = new Color(0f, 0f, 0f, 0.07f);

	private sealed class Choice
	{
		public string Caption;

		public Func<string[]> Names;

		public Func<int> Current;

		public Action<int> Set;

		public Text Value;
	}

	private static readonly string[] StyleNames = { "ARCHIPELAGO", "CONTINENT", "HIGHLANDS" };

	private static readonly string[] SizeNames = { "SMALL", "MEDIUM", "LARGE" };

	private static readonly string[] BiomeNames = { "FOREST", "WINTER", "DESERT" };

	private static readonly string[] AmountNames = { "NONE", "FEW", "NORMAL", "MANY", "LOTS" };

	private GameObject _canvas;

	private RawImage _picture;

	private GameObject _making;

	private Text _makingText;

	private Text _seed;

	private Text _status;

	private InputField _seedField;

	// "Share code copied" shows in the status line until then.
	private float _copiedUntil;

	private readonly List<Choice> _choices = new List<Choice>();

	private static RandomWorldPanel _instance;

	private void Awake()
	{
		_instance = this;
	}

	// After a new world: the start choice's points may have changed.
	internal static void RefreshOpen()
	{
		if (_instance != null)
		{
			_instance.Refresh();
		}
	}

	// Whether the start choice is offered (single player); set each frame from the open menu.
	private static bool _single;

	private void Update()
	{
		bool shown = RandomWorldMenu.PanelShown(out bool single);
		if (!shown)
		{
			if (_canvas != null && _canvas.activeSelf)
			{
				_canvas.SetActive(false);
			}
			return;
		}
		_single = single;
		if (_canvas == null)
		{
			Build();
		}
		if (!_canvas.activeSelf)
		{
			_canvas.SetActive(true);
			Refresh();
		}
		Tick();
	}

	// Reads the settings again (after a change, or a new world).
	internal void Refresh()
	{
		if (_canvas == null)
		{
			return;
		}
		foreach (Choice choice in _choices)
		{
			string[] names = choice.Names();
			int value = Mathf.Clamp(choice.Current(), 0, names.Length - 1);
			choice.Value.text = names[value];
			choice.Value.transform.parent.parent.gameObject.SetActive(choice.Caption != "START" || _single);
		}
		_seed.text = RandomWorld.Settings.Seed.ToUpperInvariant();
	}

	private void Tick()
	{
		// Ctrl+V in the seed field pastes the system's clipboard (the game's own paste only knows what it copied).
		if (_seedField != null && _seedField.isFocused && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
		{
			_seedField.text = SystemClipboard.Get().Trim();
			_seedField.MoveTextEnd(false);
		}
		Texture picture = RandomWorld.Preview;
		if (picture == null)
		{
			picture = RandomWorldMenu.ShownThumbnail();
		}
		if (_picture.texture != picture)
		{
			_picture.texture = picture;
		}
		bool busy = RandomWorld.Busy;
		_making.SetActive(busy || picture == null);
		if (busy || picture == null)
		{
			int dots = (int)(Time.unscaledTime * 3f) % 4;
			_makingText.text = "MAKING THE WORLD" + new string('.', dots) + new string(' ', 3 - dots);
		}
		_status.text = busy ? "Making the world..." : ((Time.unscaledTime < _copiedUntil) ? "Share code copied: " + RandomWorld.ShareCode(RandomWorld.Settings) : RandomWorld.Summary());
	}

	private void Build()
	{
		_canvas = UiKit.CreateCanvas("CastleStoryPlusRandomWorld", transform, 30000);
		RectTransform root = UiKit.CreateRect("Panel", _canvas.transform);
		// In the middle of the room between the menu's left bar and its sidebar, under the row of map cards.
		root.anchorMin = new Vector2(0.485f, 0f);
		root.anchorMax = new Vector2(0.485f, 0f);
		root.pivot = new Vector2(0.5f, 0f);
		root.anchoredPosition = new Vector2(0f, 70f);
		Image background = root.gameObject.AddComponent<Image>();
		background.color = LuiStyle.CastleYellow;
		background.raycastTarget = true;
		VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset((int)Pad, (int)Pad, 12, (int)Pad);
		layout.spacing = 10f;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		ContentSizeFitter fitter = root.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		Text title = Label(root, "RANDOM WORLD", 26, Ink, TextAnchor.MiddleLeft, LuiStyle.Title);
		UiKit.Fixed(title.gameObject, PictureWidth + ChoicesWidth + Pad, 30f);

		Transform body = Row(root, PictureHeight, Pad);
		BuildPicture(body);
		BuildChoices(body);
		BuildSeed(root);
	}

	private void BuildPicture(Transform parent)
	{
		RectTransform frame = UiKit.CreateRect("Picture", parent);
		UiKit.Fixed(frame.gameObject, PictureWidth, PictureHeight);
		Image border = frame.gameObject.AddComponent<Image>();
		border.color = Color.white;
		RectTransform inner = UiKit.CreateRect("Image", frame);
		UiKit.SetRect(inner, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, -8f));
		_picture = inner.gameObject.AddComponent<RawImage>();
		_picture.color = Color.white;
		RectTransform making = UiKit.CreateRect("Making", inner);
		UiKit.SetRect(making, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		Image shade = making.gameObject.AddComponent<Image>();
		shade.color = new Color(0f, 0f, 0f, 0.55f);
		shade.raycastTarget = false;
		_making = making.gameObject;
		_makingText = Label(making, string.Empty, 28, Color.white, TextAnchor.MiddleCenter, LuiStyle.Title);
		UiKit.SetRect(_makingText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		_makingText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
	}

	private void BuildChoices(Transform parent)
	{
		RectTransform column = UiKit.CreateRect("Choices", parent);
		UiKit.Fixed(column.gameObject, ChoicesWidth, PictureHeight);
		VerticalLayoutGroup layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.spacing = 4f;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		Add(column, "WORLD", () => StyleNames, () => (int)RandomWorld.Settings.Style, (int v) => Change((ref WorldSettings s) => s.Style = (WorldStyle)v));
		Add(column, "SIZE", () => SizeNames, () => (int)RandomWorld.Settings.Size, (int v) => Change((ref WorldSettings s) => s.Size = (WorldSize)v));
		Add(column, "BIOME", () => BiomeNames, () => (int)RandomWorld.Settings.Biome, (int v) => Change((ref WorldSettings s) => s.Biome = (WorldBiome)v));
		Add(column, "TREES", () => AmountNames, () => (int)RandomWorld.Settings.Trees, (int v) => Change((ref WorldSettings s) => s.Trees = (WorldAmount)v));
		Add(column, "BOULDERS", () => AmountNames, () => (int)RandomWorld.Settings.Boulders, (int v) => Change((ref WorldSettings s) => s.Boulders = (WorldAmount)v));
		Add(column, "SURFACE ORE", () => AmountNames, () => (int)RandomWorld.Settings.SurfaceOre, (int v) => Change((ref WorldSettings s) => s.SurfaceOre = (WorldAmount)v));
		Add(column, "DEEP ORE", () => AmountNames, () => (int)RandomWorld.Settings.DeepOre, (int v) => Change((ref WorldSettings s) => s.DeepOre = (WorldAmount)v));
		Add(column, "START", StartNames, () => RandomWorld.ChosenStart, (int v) =>
		{
			RandomWorld.ChosenStart = v;
			Refresh();
		});
	}

	// RANDOM, then the starting points by the numbers on the picture.
	private static string[] StartNames()
	{
		int count = RandomWorld.Busy ? 0 : RandomWorld.Starts().Count;
		string[] names = new string[count + 1];
		names[0] = "RANDOM";
		for (int i = 1; i <= count; i++)
		{
			names[i] = "POINT " + i.ToString(CultureInfo.InvariantCulture);
		}
		return names;
	}

	// One choice: its caption on the left, "<  VALUE  >" on the right; the arrows step through the values, round.
	private void Add(Transform parent, string caption, Func<string[]> names, Func<int> current, Action<int> set)
	{
		Choice choice = new Choice { Caption = caption, Names = names, Current = current, Set = set };
		Transform row = Row(parent, 40f, 6f);
		Image strip = row.gameObject.AddComponent<Image>();
		strip.color = Strip;
		row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 4, 0, 0);
		Text label = Label(row, caption, 13, InkSoft, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Flexible(label.gameObject);
		Transform stepper = Row(row, 40f, 0f);
		UiKit.Fixed(stepper.gameObject, 200f, 40f);
		Arrow(stepper, "<", () => Step(choice, -1));
		choice.Value = Label(stepper, string.Empty, 17, Ink, TextAnchor.MiddleCenter, LuiStyle.Bold);
		UiKit.Flexible(choice.Value.gameObject);
		Arrow(stepper, ">", () => Step(choice, 1));
		_choices.Add(choice);
	}

	private void Step(Choice choice, int by)
	{
		string[] names = choice.Names();
		if (names.Length < 2)
		{
			return;
		}
		int value = ((Mathf.Clamp(choice.Current(), 0, names.Length - 1) + by) % names.Length + names.Length) % names.Length;
		choice.Set(value);
	}

	// WorldSettings is a struct: the change is made to a copy and handed back.
	private delegate void Edit(ref WorldSettings settings);

	private void Change(Edit apply)
	{
		WorldSettings settings = RandomWorld.Settings;
		apply(ref settings);
		RandomWorldMenu.Change(settings);
		Refresh();
	}

	private void BuildSeed(Transform parent)
	{
		Transform row = Row(parent, 44f, 10f);
		Image strip = row.gameObject.AddComponent<Image>();
		strip.color = Strip;
		row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 6, 0, 0);
		Text caption = Label(row, "SEED", 13, InkSoft, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Fixed(caption.gameObject, 50f, 44f);
		_seed = Label(row, string.Empty, 15, Ink, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Fixed(_seed.gameObject, 360f, 44f);
		_seedField = UiKit.CreateInputField(row, "Type or paste a seed or share code...", 230f);
		UiKit.Fixed(_seedField.gameObject, 230f, 32f);
		_seedField.onEndEdit.AddListener((string text) =>
		{
			_seedField.text = string.Empty;
			Use(text);
		});
		Button copy = Button(row, "COPY", 70f, () =>
		{
			SystemClipboard.Set(RandomWorld.ShareCode(RandomWorld.Settings));
			_copiedUntil = Time.unscaledTime + 2f;
		});
		UiKit.Fixed(copy.gameObject, 70f, 32f);
		Button paste = Button(row, "PASTE", 70f, () => Use(SystemClipboard.Get()));
		UiKit.Fixed(paste.gameObject, 70f, 32f);
		Button roll = Button(row, "NEW SEED", 110f, () => Change((ref WorldSettings s) => s.Seed = RandomWorld.NewSeed()));
		UiKit.Fixed(roll.gameObject, 110f, 32f);
		_status = Label(parent, string.Empty, 12, InkSoft, TextAnchor.MiddleLeft, LuiStyle.Regular);
		_status.horizontalOverflow = HorizontalWrapMode.Wrap;
		UiKit.Fixed(_status.gameObject, PictureWidth + ChoicesWidth + Pad, 18f);
	}

	// A typed or pasted share code (every setting and the seed) or a plain seed.
	private void Use(string text)
	{
		text = (text ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return;
		}
		if (RandomWorld.TryParseShareCode(text, out WorldSettings shared))
		{
			RandomWorldMenu.Change(shared);
			Refresh();
			return;
		}
		string seed = text.ToLowerInvariant();
		if (seed != RandomWorld.Settings.Seed)
		{
			Change((ref WorldSettings s) => s.Seed = seed);
		}
	}

	private static Transform Row(Transform parent, float height, float spacing)
	{
		Transform row = UiKit.CreateRow(parent, height);
		HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
		layout.spacing = spacing;
		layout.childForceExpandHeight = true;
		return row;
	}

	private static Text Label(Transform parent, string text, int size, Color color, TextAnchor alignment, Font font)
	{
		Text label = UiKit.CreateText(parent, text, size, color, alignment);
		if (font != null)
		{
			label.font = font;
		}
		return label;
	}

	private static void Arrow(Transform parent, string text, Action onClick)
	{
		Button button = Button(parent, text, 36f, onClick);
		UiKit.Fixed(button.gameObject, 36f, 40f);
		Text label = button.GetComponentInChildren<Text>();
		label.fontSize = 20;
	}

	// A dark button with yellow text, lighter under the mouse.
	private static Button Button(Transform parent, string text, float width, Action onClick)
	{
		Button button = UiKit.CreateButton(parent, text, width, () =>
		{
			onClick();
			RandomWorldMenu.PlaySelect();
		});
		ColorBlock colors = button.colors;
		colors.normalColor = Ink;
		colors.highlightedColor = new Color(0.25f, 0.22f, 0.12f, 1f);
		colors.pressedColor = new Color(0.4f, 0.33f, 0.1f, 1f);
		button.colors = colors;
		Text label = button.GetComponentInChildren<Text>();
		label.color = LuiStyle.CastleYellow;
		label.font = LuiStyle.Bold ?? label.font;
		label.fontSize = 14;
		return button;
	}
}
