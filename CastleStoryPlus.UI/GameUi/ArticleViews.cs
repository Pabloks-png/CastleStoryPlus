using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Brix.Assets;
using Brix.UI.Icons;
using CastleStoryPlus.LuaUi;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.GameUi;

// An article drawn into a panel (the help): labels, subtitles, framed boxes, pictures, dividers, spacers and the
// links to other help pages ("next"), in the look of the game's article panels.
internal class CsArticleView
{
	private readonly List<KeyValuePair<Text, GameArticles.Item>> _texts = new List<KeyValuePair<Text, GameArticles.Item>>();

	private readonly Action<string> _select;

	public RectTransform Root { get; private set; }

	private CsArticleView(Action<string> select)
	{
		_select = select;
	}

	// select: what a link to another article does (its file name).
	public static CsArticleView Build(Transform parent, GameArticles.Article article, float width, Action<string> select)
	{
		CsArticleView view = new CsArticleView(select);
		RectTransform root = UiKit.CreateRect("Article", parent);
		VerticalLayoutGroup layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		LayoutElement size = root.gameObject.AddComponent<LayoutElement>();
		size.minWidth = width;
		size.preferredWidth = width;
		view.Root = root;
		if (article != null)
		{
			foreach (GameArticles.Item item in article.Items)
			{
				view.Add(item, width);
			}
		}
		view.Refresh();
		return view;
	}

	private void Add(GameArticles.Item item, float width)
	{
		switch (item.Type)
		{
		case "Label":
			AddText(item, 14, LuiStyle.Gray, TextAnchor.UpperLeft, null, new RectOffset(0, 0, 4, 4));
			break;
		case "MiniLabel":
			AddText(item, 12, LuiStyle.Gray, TextAnchor.UpperLeft, null, new RectOffset(0, 0, 2, 2));
			break;
		case "Subtitle":
			AddText(item, 18, LuiStyle.CastleYellow, (item.Field("alignment") == "Alignment.MiddleCenter") ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, LuiStyle.Title, new RectOffset(0, 0, 6, 4));
			break;
		case "SquaredLabel":
			AddSquaredLabel(item);
			break;
		case "Divider":
			AddText(item, 13, LuiStyle.CastleYellow, TextAnchor.MiddleLeft, LuiStyle.Bold, new RectOffset(0, 0, 6, 2));
			LuiWidgets.Divider(Root, vertical: false);
			break;
		case "Spacer":
			AddSpace(item.Number("height", 8f));
			break;
		case "Splitter":
			AddSpace(4f);
			LuiWidgets.Divider(Root, vertical: false);
			AddSpace(4f);
			break;
		case "RawImage":
			AddImage(item, width);
			break;
		case "ArticleLink":
		case "ObjectiveButton":
			AddLink(item, item.Type == "ObjectiveButton");
			break;
		}
	}

	private Text NewText(Transform parent, int size, Color color, TextAnchor alignment, Font font)
	{
		Text text = LuiWidgets.Label(parent, string.Empty, size, color, alignment, font);
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.supportRichText = true;
		return text;
	}

	private RectTransform Row(RectOffset padding)
	{
		RectTransform row = UiKit.CreateRect("Row", Root);
		VerticalLayoutGroup layout = row.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = padding;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		return row;
	}

	private void AddText(GameArticles.Item item, int size, Color color, TextAnchor alignment, Font font, RectOffset padding)
	{
		if (item.Text == null)
		{
			return;
		}
		_texts.Add(new KeyValuePair<Text, GameArticles.Item>(NewText(Row(padding), size, color, alignment, font), item));
	}

	private void AddSquaredLabel(GameArticles.Item item)
	{
		RectTransform box = Row(new RectOffset(8, 8, 6, 6));
		Image background = box.gameObject.AddComponent<Image>();
		background.color = new Color(0f, 0f, 0f, 0.5f);
		background.raycastTarget = false;
		box.gameObject.AddComponent<LayoutElement>().minHeight = item.Number("height", 100f);
		_texts.Add(new KeyValuePair<Text, GameArticles.Item>(NewText(box, 14, LuiStyle.Gray, TextAnchor.MiddleLeft, null), item));
	}

	private void AddSpace(float height)
	{
		RectTransform space = UiKit.CreateRect("Space", Root);
		LayoutElement size = space.gameObject.AddComponent<LayoutElement>();
		size.minHeight = height;
		size.preferredHeight = height;
	}

	// The help's pictures (Info/Lua/LUI/Data/Tutorial/Textures), at most as wide as the article, centred.
	private void AddImage(GameArticles.Item item, float width)
	{
		string path = item.Field("path");
		Texture2D texture = null;
		if (!string.IsNullOrEmpty(path))
		{
			try
			{
				texture = SubAsset_Texture2D.GetAssetSync(path, clearOnSceneChanged: true);
			}
			catch (Exception ex)
			{
				LuaBridge.LogOnce("article image", path + ": " + ex.Message);
			}
		}
		if (texture == null)
		{
			return;
		}
		RectTransform row = UiKit.CreateRect("Image", Root);
		HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.padding = new RectOffset(0, 0, 4, 4);
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = false;
		float shown = Mathf.Min(item.Number("width", 224f), width);
		float height = shown * texture.height / Mathf.Max(1f, texture.width);
		RectTransform picture = UiKit.CreateRect("Picture", row);
		RawImage image = picture.gameObject.AddComponent<RawImage>();
		image.texture = texture;
		image.raycastTarget = false;
		UiKit.Fixed(picture.gameObject, shown, height);
		picture.gameObject.AddComponent<Outline>().effectColor = LuiStyle.CastleYellow;
	}

	// A link to another help page (ArticleLink: the page's title), or the page's "next" button (ObjectiveButton).
	private void AddLink(GameArticles.Item item, bool next)
	{
		string id = item.Field("article_id");
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		Transform line = UiKit.CreateRow(Root, next ? 32f : 26f);
		HorizontalLayoutGroup layout = line.GetComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(20, next ? 20 : 0, 4, 4);
		Image background = line.gameObject.AddComponent<Image>();
		background.color = new Color(0f, 0f, 0f, 0.5f);
		RectTransform icon = UiKit.CreateRect("Arrow", line);
		Image arrow = icon.gameObject.AddComponent<Image>();
		arrow.sprite = IconKeys.UI_Right.Get64();
		arrow.color = LuiStyle.CastleYellow;
		arrow.preserveAspect = true;
		arrow.raycastTarget = false;
		UiKit.Fixed(icon.gameObject, 16f, 16f);
		Text text = NewText(line, next ? 14 : 12, LuiStyle.CastleYellow, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Flexible(text.gameObject);
		GameArticles.Item shown = item;
		if (!next)
		{
			// A link shows the title of the page it leads to.
			GameArticles.Article target = GameArticles.Get("Help", id);
			shown = new GameArticles.Item { Type = item.Type, Text = (target != null) ? target.Title : null };
		}
		_texts.Add(new KeyValuePair<Text, GameArticles.Item>(text, shown));
		ArticleButton button = line.gameObject.AddComponent<ArticleButton>();
		button.Background = background;
		button.Enabled = () => true;
		button.OnClick = () => _select?.Invoke(id);
	}

	// Reads every text again.
	public void Refresh()
	{
		foreach (KeyValuePair<Text, GameArticles.Item> pair in _texts)
		{
			string text = (pair.Value.Text != null) ? pair.Value.Text() : string.Empty;
			if (pair.Key.text != text)
			{
				pair.Key.text = text;
			}
		}
	}
}

// A UiArticle: the page drawn again when it changes, its texts read again otherwise.
internal class CsArticleItemView : CsItemView
{
	private UiArticle _item;

	private string _shown;

	private CsArticleView _view;

	protected override UiItem Item
	{
		get { return _item; }
	}

	public static CsArticleItemView Create(Transform parent, UiArticle item)
	{
		RectTransform rect = UiKit.CreateRect("Article " + item.Id, parent);
		VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		CsArticleItemView view = rect.gameObject.AddComponent<CsArticleItemView>();
		view._item = item;
		return view;
	}

	protected override void Read()
	{
		string page = (_item.Page != null) ? _item.Page() : null;
		if (page == _shown && _view != null)
		{
			_view.Refresh();
			return;
		}
		_shown = page;
		if (_view != null)
		{
			Destroy(_view.Root.gameObject);
		}
		_view = CsArticleView.Build(transform, GameArticles.Get(_item.Folder, page), _item.Width, (string id) =>
		{
			_item.Select?.Invoke(id);
			MarkDirty();
			GameUiState.Announce();
		});
		ScrollRect scroll = GetComponentInParent<ScrollRect>();
		if (scroll != null)
		{
			scroll.verticalNormalizedPosition = 1f;
		}
	}
}
