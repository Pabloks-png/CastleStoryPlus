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

// The game's articles (Info/Lua/LUI/Article: the tooltips of the task popup's buttons and the help pages) read
// as data, without running them: each file answers a list of items ({ type = "Label", Text = ||GetLocalized("##key")
// }, ...), almost all of them plain keys and numbers, which are read here. An item whose text is worked out by
// Lua (the resource and recipe tooltips) is left out. Read once per file.
internal static class GameArticles
{
	internal sealed class Item
	{
		public string Type;

		// The item's text (Text, else ButtonText), or null.
		public Func<string> Text;

		public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();

		public string Field(string name)
		{
			return Fields.TryGetValue(name, out string value) ? value : null;
		}

		public float Number(string name, float fallback)
		{
			string value = Field(name);
			return (value != null && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n)) ? n : fallback;
		}
	}

	internal sealed class Article
	{
		public string Name;

		public Func<string> Title;

		public readonly List<Item> Items = new List<Item>();
	}

	private static readonly Dictionary<string, Article> Cache = new Dictionary<string, Article>();

	private static readonly Regex Localized = new Regex("^(?:\\|\\|)?\\s*GetLocalized\\(\\s*\"([^\"]*)\"\\s*\\)$");

	private static readonly Regex Literal = new Regex("^(?:\\|\\|)?\\s*\"((?:[^\"\\\\]|\\\\.)*)\"$");

	private static readonly Regex Field = new Regex("^\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(.*?)\\s*$");

	// An article of a folder (Tooltip, Help) by its file name ("Task_Build_Goals", "WelcomeToCastleStory"), or null.
	internal static Article Get(string folder, string name)
	{
		string key = folder + "/" + name;
		if (Cache.TryGetValue(key, out Article article))
		{
			return article;
		}
		article = null;
		try
		{
			string path = Path.Combine(Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath, "Info/Lua/LUI/Article"), folder), name + ".lua");
			if (File.Exists(path))
			{
				article = Parse(name, File.ReadAllText(path));
			}
		}
		catch (Exception ex)
		{
			LuaBridge.LogOnce("article " + key, ex.Message);
		}
		Cache[key] = article;
		return article;
	}

	// A tooltip article's texts, one paragraph each, for a plain tooltip; null when there is none.
	internal static string TooltipText(string name)
	{
		Article article = Get("Tooltip", name);
		if (article == null)
		{
			return null;
		}
		StringBuilder text = new StringBuilder();
		foreach (Item item in article.Items)
		{
			string part = (item.Text != null) ? item.Text() : null;
			if (string.IsNullOrEmpty(part))
			{
				continue;
			}
			if (text.Length > 0)
			{
				text.Append("\n");
			}
			text.Append(part);
		}
		return (text.Length > 0) ? text.ToString() : null;
	}

	// The button's tooltip from an article: its label in yellow, then the article's text.
	internal static Func<string> Tooltip(Func<string> label, string name)
	{
		return () =>
		{
			string text = TooltipText(name);
			string title = (label != null) ? label() : null;
			if (text == null)
			{
				return title;
			}
			return string.IsNullOrEmpty(title) ? text : "<color=#ffc50a><b>" + title + "</b></color>\n" + text;
		};
	}

	private static Article Parse(string name, string source)
	{
		string text = StripComments(source);
		int start = text.IndexOf("return", StringComparison.Ordinal);
		int open = (start < 0) ? -1 : text.IndexOf('{', start);
		if (open < 0)
		{
			return null;
		}
		Article article = new Article { Name = name };
		// The returned table: fields at its own level (Title), items one level down.
		int depth = 0;
		int itemStart = -1;
		StringBuilder top = new StringBuilder();
		for (int i = open; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '"')
			{
				int end = SkipString(text, i);
				if (depth == 1)
				{
					top.Append(text, i, end - i + 1);
				}
				i = end;
				continue;
			}
			if (c == '{')
			{
				depth++;
				if (depth == 2)
				{
					itemStart = i + 1;
				}
				continue;
			}
			if (c == '}')
			{
				if (depth == 2 && itemStart >= 0)
				{
					article.Items.Add(ParseItem(text.Substring(itemStart, i - itemStart)));
					itemStart = -1;
				}
				depth--;
				if (depth == 0)
				{
					break;
				}
				continue;
			}
			if (depth == 1)
			{
				top.Append(c);
			}
		}
		foreach (string line in SplitFields(top.ToString()))
		{
			Match match = Field.Match(line);
			if (match.Success && (match.Groups[1].Value == "Title" || match.Groups[1].Value == "title"))
			{
				article.Title = TextOf(match.Groups[2].Value);
			}
		}
		return article;
	}

	private static Item ParseItem(string body)
	{
		Item item = new Item();
		foreach (string part in SplitFields(body))
		{
			Match match = Field.Match(part);
			if (!match.Success)
			{
				continue;
			}
			string value = match.Groups[2].Value;
			Match literal = Literal.Match(value);
			item.Fields[match.Groups[1].Value] = literal.Success ? literal.Groups[1].Value : value;
			if (match.Groups[1].Value == "type")
			{
				item.Type = literal.Success ? literal.Groups[1].Value : value;
			}
		}
		if (item.Fields.TryGetValue("Text", out string _))
		{
			item.Text = TextOf(Raw(body, "Text"));
		}
		else if (item.Fields.ContainsKey("ButtonText"))
		{
			item.Text = TextOf(Raw(body, "ButtonText"));
		}
		return item;
	}

	// A field's value as written (before a literal's quotes were taken off).
	private static string Raw(string body, string name)
	{
		foreach (string part in SplitFields(body))
		{
			Match match = Field.Match(part);
			if (match.Success && match.Groups[1].Value == name)
			{
				return match.Groups[2].Value;
			}
		}
		return null;
	}

	// A localized key or a plain string; anything worked out by Lua gives null.
	private static Func<string> TextOf(string value)
	{
		if (value == null)
		{
			return null;
		}
		Match localized = Localized.Match(value.Trim());
		if (localized.Success)
		{
			string key = localized.Groups[1].Value;
			return () => GameUiState.Text(key);
		}
		Match literal = Literal.Match(value.Trim());
		if (literal.Success)
		{
			string shown = literal.Groups[1].Value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"");
			return () => shown;
		}
		return null;
	}

	// The fields of a table's body, split at its commas (not those inside strings or brackets).
	private static List<string> SplitFields(string body)
	{
		List<string> parts = new List<string>();
		int depth = 0;
		int from = 0;
		for (int i = 0; i < body.Length; i++)
		{
			char c = body[i];
			if (c == '"')
			{
				i = SkipString(body, i);
				continue;
			}
			if (c == '(' || c == '{' || c == '[')
			{
				depth++;
			}
			else if (c == ')' || c == '}' || c == ']')
			{
				depth--;
			}
			else if (c == ',' && depth == 0)
			{
				parts.Add(body.Substring(from, i - from));
				from = i + 1;
			}
		}
		if (from < body.Length)
		{
			parts.Add(body.Substring(from));
		}
		return parts;
	}

	// The index of a string's closing quote.
	private static int SkipString(string text, int open)
	{
		for (int i = open + 1; i < text.Length; i++)
		{
			if (text[i] == '\\')
			{
				i++;
			}
			else if (text[i] == '"')
			{
				return i;
			}
		}
		return text.Length - 1;
	}

	private static string StripComments(string source)
	{
		string text = Regex.Replace(source, "--\\[\\[.*?\\]\\]", string.Empty, RegexOptions.Singleline);
		StringBuilder result = new StringBuilder();
		foreach (string line in text.Split('\n'))
		{
			int comment = CommentStart(line);
			result.Append((comment < 0) ? line : line.Substring(0, comment)).Append('\n');
		}
		return result.ToString();
	}

	// Where a line's "--" comment starts, outside strings; -1 when it has none.
	private static int CommentStart(string line)
	{
		bool inString = false;
		for (int i = 0; i < line.Length - 1; i++)
		{
			char c = line[i];
			if (c == '\\' && inString)
			{
				i++;
				continue;
			}
			if (c == '"')
			{
				inString = !inString;
			}
			else if (!inString && c == '-' && line[i + 1] == '-')
			{
				return i;
			}
		}
		return -1;
	}
}
