using Brix.Engine;
using Brix.External.Signals;
using Brix.UI.Icons;
using CastleStoryPlus.LuaUi;

namespace CastleStoryPlus.GameUi;

// The in-game help (GameMenu.lua's tutorial handle, Menu_Help.lua): a sidebar beside the right bar with the help's
// pages (LUI/Article/Help), opened from the right bar in a single-player sandbox game, or by the game's "show help"
// (UIGame.hk_ShowHelp). It starts on the welcome page; links and the pages' "next" buttons change the page, which
// is kept while the help is closed.
internal static class GameHelp
{
	private const string FirstPage = "WelcomeToCastleStory";

	private static string _page = FirstPage;

	private static bool _listening;

	private static UiPanel _panel;

	internal static bool Available
	{
		get { return GameParam.Gamemode != null && GameParam.Gamemode.rawDisplayName == "##mode_sandbox" && !Neo.IsMultiplayer; }
	}

	internal static UiPanel Build(GameMenuModel menu)
	{
		UiPanel panel = new UiPanel
		{
			Id = "help",
			Title = () =>
			{
				GameArticles.Article article = GameArticles.Get("Help", _page);
				return (article != null && article.Title != null) ? article.Title() : GameUiState.Text("##gamemenu_ingamehelp_title");
			},
			Group = menu.Right
		};
		panel.Section("page", null).Add(new UiArticle
		{
			Id = "helpPage",
			Page = () => _page,
			Select = (string id) => _page = id
		});
		_panel = panel;
		if (!_listening)
		{
			_listening = true;
			UIGameObserver.lua_ShowHelp._signal.Connect(Show);
		}
		return panel;
	}

	internal static UiButton Button(UiPanel panel)
	{
		return new UiButton
		{
			Id = "tutorial",
			Visible = () => Available,
			Label = () => GameUiState.Text("##gamemenu_ingamehelp_title"),
			Icon = () => GameUiState.Icon(IconKeys.UI_Help),
			Selected = () => panel.IsOpen,
			OnClick = (bool shift, bool ctrl) => panel.Toggle()
		};
	}

	// UIGame.hk_ShowHelp: the help opens (on the page it was left at).
	private static void Show()
	{
		if (_panel != null && CsGameMenu.Model != null)
		{
			_panel.Open();
		}
	}

	internal static void Reset()
	{
		_page = FirstPage;
	}
}
