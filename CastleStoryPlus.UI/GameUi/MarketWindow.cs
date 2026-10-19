using System.Collections.Generic;
using Brix.Game.Components;
using CastleStoryPlus.LuaUi;
using CastleStoryPlus.Market;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.GameUi;

// The market's window: "What do you need?" is every good with what the stockpiles hold of it; the chosen good's ways
// to pay are rows under it ("12 Log", the one the team can pay most often marked "best"), each with how many to keep
// ("-  20  +", Off while it is not the good's standing trade) and a "+" that trades once (keeps what the stockpiles
// hold now and one trade more). A keep number makes it a standing trade: the market trades for the good while the
// stockpiles hold less. The standing trades are chips at the bottom ("Log -> Rope keep 20"; a click chooses that good).
// The trades are the market's looping queue with a loop limit per trade (MarketOrders).
internal class CsMarketWindow : CsStationWindow
{
	private sealed class GoodSlot
	{
		public MarketPrices.Good Good;

		public Image Background;

		public Image Icon;

		public Text Count;
	}

	private sealed class PayRow
	{
		public MarketPrices.Trade Trade;

		public Image Background;

		public Image Icon;

		public Text Text;

		public UiCounter Counter;
	}

	private sealed class StandingChip
	{
		public GameObject Root;

		public Image Give;

		public Image Get;

		public Text Text;

		public MarketPrices.Good Good;
	}

	// Most ways to pay a good shows, the best first.
	private const int MaxRows = 6;

	private const int Columns = 7;

	private const float CellGap = 5f;

	private static readonly Color Bad = new Color(0.9f, 0.36f, 0.26f, 1f);

	private readonly List<GoodSlot> _goods = new List<GoodSlot>();

	private readonly List<PayRow> _rows = new List<PayRow>();

	private readonly List<StandingChip> _chips = new List<StandingChip>();

	private MarketPrices.Good _chosen;

	private Text _payCaption;

	private RectTransform _payList;

	private Text _running;

	private RectTransform _chipRow;

	private Text _noStanding;

	private MarketPrices.Good _rowsFor;

	private int _shownChips = -1;

	protected override float Width
	{
		get { return 410f; }
	}

	public static CsMarketWindow Create(Transform parent)
	{
		CsMarketWindow window = new GameObject("CS market", typeof(RectTransform)).AddComponent<CsMarketWindow>();
		window.Setup(parent, "CS market", BuildCatalog.ModIcon("market", "_Machine_Shop"), "Market");
		MarketPanel.Elsewhere = true;
		return window;
	}

	protected override void OnDestroy()
	{
		MarketPanel.Elsewhere = false;
		base.OnDestroy();
	}

	protected override CraftingStation Pick(CraftingStation selected)
	{
		return selected as MarketStation;
	}

	protected override void OnStation(CraftingStation station)
	{
		_chosen = null;
		_rowsFor = null;
	}

	private MarketStation Market
	{
		get { return Station as MarketStation; }
	}

	// ---- build

	protected override void Build()
	{
		Caption(Body, "What do you need?");
		float cell = Mathf.Floor((Width - 24f - CellGap * (Columns - 1)) / Columns);
		RectTransform grid = Grid(Body, new Vector2(cell, cell), CellGap, Columns);
		foreach (MarketPrices.Good good in MarketPrices.Goods)
		{
			if (good.Buyable)
			{
				_goods.Add(BuildGood(grid, good, cell));
			}
		}
		Line(Body);
		_payCaption = Caption(Body, string.Empty);
		_payList = Column(Body, 4f);
		Line(Body);
		Transform head = Row(Body, 16f, 6f);
		Text standing = Caption(head, "Standing trades");
		UiKit.Flexible(standing.gameObject);
		_running = LuiWidgets.Label(head, string.Empty, LuiStyle.SmallFontSize, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Regular);
		UiKit.Fixed(_running.gameObject, 120f, 16f);
		_noStanding = Note(Body, "None yet: a keep number above makes one.");
		_chipRow = Grid(Body, new Vector2(124f, 26f), 6f, 3);
	}

	private GoodSlot BuildGood(Transform grid, MarketPrices.Good good, float size)
	{
		RectTransform slot = CsRowParts.Slot(grid, size);
		GoodSlot view = new GoodSlot { Good = good, Background = slot.GetComponent<Image>(), Icon = CsRowParts.Picture(slot, size * 0.78f) };
		view.Background.raycastTarget = true;
		view.Count = LuiWidgets.Label(slot, string.Empty, 10, LuiStyle.White, TextAnchor.LowerRight, LuiStyle.Bold);
		RectTransform count = view.Count.rectTransform;
		count.anchorMin = Vector2.zero;
		count.anchorMax = Vector2.one;
		count.offsetMin = new Vector2(2f, 1f);
		count.offsetMax = new Vector2(-3f, -1f);
		view.Count.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
		Clickable(slot.gameObject, () =>
		{
			_chosen = good;
			Soon();
		}, () => GoodTooltip(good));
		return view;
	}

	private void BuildRows(MarketStation market, MarketPrices.Good good)
	{
		List<GameObject> old = new List<GameObject>();
		foreach (Transform child in _payList)
		{
			old.Add(child.gameObject);
		}
		foreach (GameObject go in old)
		{
			DestroyImmediate(go);
		}
		_rows.Clear();
		_rowsFor = good;
		if (good != null)
		{
			foreach (MarketPrices.Trade trade in Payments(market, good))
			{
				_rows.Add(BuildRow(good, trade));
			}
		}
		LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
	}

	private PayRow BuildRow(MarketPrices.Good good, MarketPrices.Trade trade)
	{
		PayRow row = new PayRow { Trade = trade };
		Transform rect = Row(_payList, 30f, 6f);
		HorizontalLayoutGroup layout = rect.GetComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(6, 4, 3, 3);
		layout.childAlignment = TextAnchor.MiddleLeft;
		row.Background = rect.gameObject.AddComponent<Image>();
		UiTheme.Round(row.Background, UiTheme.BgBlock, UiTheme.SlotRadius);
		row.Icon = Picture(rect, 22f);
		Sprite sprite = (trade.Give.Icon != null) ? trade.Give.Icon.Get64() : null;
		row.Icon.sprite = sprite;
		row.Icon.enabled = sprite != null;
		row.Text = LuiWidgets.Label(rect, string.Empty, LuiStyle.FontSize, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		row.Text.supportRichText = true;
		UiKit.Flexible(row.Text.gameObject);
		Clickable(row.Text.gameObject, null, () => RowTooltip(good, trade));
		Text keep = LuiWidgets.Label(rect, "keep", LuiStyle.SmallFontSize, UiTheme.TextDim, TextAnchor.MiddleRight, LuiStyle.Regular);
		UiKit.Fixed(keep.gameObject, 30f, 24f);
		row.Counter = UiCounter.Create(rect, 84f, (int delta) =>
		{
			int now = MarketOrders.KeepWith(Market, good, trade.Give);
			if (MarketOrders.SetKeep(Market, good, trade.Give, MarketOrders.NextKeep(now, delta)))
			{
				Soon();
			}
		});
		row.Counter.SetFontSize(12);
		Clickable(row.Counter.gameObject, null, () => "Keep this many " + good.Name + " in stock, paid with " + trade.Give.Name + ": the market trades for it while the stockpiles hold less (Shift: 10 at a time). Off: no standing trade.");
		Button once = UiKit.CreateButton(rect, "+", 24f, () =>
		{
			int target = MarketOrders.Stock(Market, good) + MarketPrices.Output(trade);
			if (MarketOrders.SetKeep(Market, good, trade.Give, Mathf.Max(target, MarketOrders.KeepWith(Market, good, trade.Give))))
			{
				Soon();
			}
		});
		UiKit.Fixed(once.gameObject, 24f, 24f);
		// Yellow, apart from the counter's own "+".
		RoundButton(once, UiTheme.SlotRadius);
		ColorBlock colors = once.colors;
		colors.normalColor = LuiStyle.CastleYellow;
		colors.highlightedColor = new Color(1f, 0.85f, 0.35f, 1f);
		colors.pressedColor = new Color(0.85f, 0.65f, 0.05f, 1f);
		once.colors = colors;
		Text plus = once.GetComponentInChildren<Text>();
		if (plus != null)
		{
			plus.color = UiTheme.TextOnYellow;
			plus.fontSize = 16;
		}
		Clickable(once.gameObject, null, () => "Trade once: " + trade.GiveAmount + " " + trade.Give.Name + " for " + MarketPrices.Output(trade) + " " + good.Name + " (keeps what the stockpiles hold now and one trade more).");
		return row;
	}

	// The ways to pay, the one the team can pay most often first, then the good's standing payment if it is not among
	// them.
	private static List<MarketPrices.Trade> Payments(MarketStation market, MarketPrices.Good good)
	{
		List<MarketPrices.Trade> all = MarketOrders.Payments(good);
		all.Sort((MarketPrices.Trade a, MarketPrices.Trade b) => Trades(market, b).CompareTo(Trades(market, a)));
		List<MarketPrices.Trade> shown = all.GetRange(0, Mathf.Min(MaxRows, all.Count));
		MarketPrices.Trade standing = MarketOrders.StandingFor(market, good);
		if (standing != null && !shown.Exists((MarketPrices.Trade t) => t.Give == standing.Give))
		{
			MarketPrices.Trade own = all.Find((MarketPrices.Trade t) => t.Give == standing.Give);
			if (own != null)
			{
				shown.Add(own);
			}
		}
		return shown;
	}

	private static float Trades(MarketStation market, MarketPrices.Trade trade)
	{
		return (float)MarketOrders.Stock(market, trade.Give) / Mathf.Max(1, trade.GiveAmount);
	}

	private StandingChip BuildChip()
	{
		RectTransform rect = UiKit.CreateRect("Standing", _chipRow);
		HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(6, 6, 2, 2);
		layout.spacing = 3f;
		layout.childAlignment = TextAnchor.MiddleLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = false;
		StandingChip chip = new StandingChip { Root = rect.gameObject };
		Image background = rect.gameObject.AddComponent<Image>();
		UiTheme.Round(background, UiTheme.BgSlot, UiTheme.PillRadius);
		chip.Give = Picture(rect, 20f);
		Text arrow = LuiWidgets.Label(rect, "→", LuiStyle.SmallFontSize, UiTheme.TextMuted, TextAnchor.MiddleCenter, LuiStyle.Regular);
		UiKit.Fixed(arrow.gameObject, 10f, 16f);
		chip.Get = Picture(rect, 20f);
		chip.Text = LuiWidgets.Label(rect, string.Empty, LuiStyle.SmallFontSize, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		Clickable(rect.gameObject, () =>
		{
			if (chip.Good != null)
			{
				_chosen = chip.Good;
				Soon();
			}
		}, () => (chip.Good != null) ? "Keeping " + chip.Good.Name + " in stock. Click to change it." : null);
		_chips.Add(chip);
		return chip;
	}

	// ---- read

	protected override void Read(CraftingStation station)
	{
		MarketStation market = (MarketStation)station;
		List<MarketPrices.Trade> standing = MarketOrders.Standing(market);
		standing.RemoveAll((MarketPrices.Trade t) => t == null);
		if (_chosen == null)
		{
			_chosen = (standing.Count > 0) ? standing[0].Get : FirstGood();
		}
		MarketPrices.Trade current = (market.Looping && market.CurrentRecipe != null) ? MarketPrices.TradeFor(market.CurrentRecipe.Info) : null;
		SetSubtitle((current != null) ? "Trading for " + current.Get.Name : (standing.Count > 0) ? "Waiting: every good has enough" : "No standing trades");

		foreach (GoodSlot slot in _goods)
		{
			Sprite sprite = (slot.Good.Icon != null) ? slot.Good.Icon.Get64() : null;
			slot.Icon.sprite = sprite;
			slot.Icon.enabled = sprite != null;
			CsRowParts.SetText(slot.Count, MarketOrders.Stock(market, slot.Good).ToString());
			bool chosen = slot.Good == _chosen;
			bool kept = standing.Exists((MarketPrices.Trade t) => t.Get == slot.Good);
			slot.Background.color = chosen ? UiTheme.BgSelectedRow : (kept ? new Color(1f, 197f / 255f, 10f / 255f, 0.08f) : UiTheme.BgSlot);
			Outline outline = slot.Background.GetComponent<Outline>() ?? slot.Background.gameObject.AddComponent<Outline>();
			outline.effectColor = LuiStyle.CastleYellow;
			outline.effectDistance = new Vector2(1.5f, -1.5f);
			outline.enabled = chosen;
		}

		bool changed = false;
		if (_chosen != _rowsFor || _rows.Count == 0)
		{
			BuildRows(market, _chosen);
			changed = true;
		}
		if (_chosen != null)
		{
			int output = (_rows.Count > 0) ? MarketPrices.Output(_rows[0].Trade) : 0;
			CsRowParts.SetText(_payCaption, ((output > 0) ? output + " " : string.Empty).ToUpperInvariant() + _chosen.Name.ToUpperInvariant() + " FOR");
		}
		MarketPrices.Trade best = (_chosen != null) ? MarketOrders.BestPayment(market, _chosen) : null;
		bool full = market.QueueCount >= MarketOrders.MaxQueue;
		foreach (PayRow row in _rows)
		{
			int keep = MarketOrders.KeepWith(market, _chosen, row.Trade.Give);
			bool lacking = MarketOrders.Stock(market, row.Trade.Give) < row.Trade.GiveAmount;
			string text = "<color=" + UiTheme.Hex(lacking ? Bad : LuiStyle.White) + ">" + row.Trade.GiveAmount + " " + row.Trade.Give.Name + "</color>";
			if (best != null && row.Trade.Give == best.Give)
			{
				text += "  <size=10><color=" + UiTheme.Hex(UiTheme.Good) + ">best</color></size>";
			}
			CsRowParts.SetText(row.Text, text);
			row.Counter.Set((keep > 0) ? keep.ToString() : "Off");
			row.Background.color = (keep > 0) ? UiTheme.BgSelectedRow : UiTheme.BgBlock;
			row.Counter.gameObject.SetActive(keep > 0 || !full);
		}

		CsRowParts.SetText(_running, (standing.Count == 0) ? string.Empty : (current != null) ? standing.Count + " kept · 1 trading" : standing.Count + " kept");
		_noStanding.gameObject.SetActive(standing.Count == 0);
		while (_chips.Count < standing.Count)
		{
			BuildChip();
			changed = true;
		}
		for (int i = 0; i < _chips.Count; i++)
		{
			StandingChip chip = _chips[i];
			bool shown = i < standing.Count;
			if (chip.Root.activeSelf != shown)
			{
				chip.Root.SetActive(shown);
			}
			if (!shown)
			{
				chip.Good = null;
				continue;
			}
			MarketPrices.Trade trade = standing[i];
			chip.Good = trade.Get;
			SetIcon(chip.Give, trade.Give);
			SetIcon(chip.Get, trade.Get);
			int keep = MarketOrders.Keep(market, trade);
			bool enough = keep > 0 && MarketOrders.Stock(market, trade.Get) >= keep;
			CsRowParts.SetText(chip.Text, "keep " + ((keep > 0) ? keep.ToString() : "∞"));
			chip.Text.color = enough ? UiTheme.Good : LuiStyle.White;
		}
		int shownChips = Mathf.Min(_chips.Count, standing.Count);
		if (changed || shownChips != _shownChips)
		{
			// The window's background is sized by its layout: again once rows or chips come or go.
			_shownChips = shownChips;
			LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
		}
	}

	private static void SetIcon(Image image, MarketPrices.Good good)
	{
		Sprite sprite = (good.Icon != null) ? good.Icon.Get64() : null;
		image.sprite = sprite;
		image.enabled = sprite != null;
	}

	private static MarketPrices.Good FirstGood()
	{
		foreach (MarketPrices.Good good in MarketPrices.Goods)
		{
			if (good.Buyable)
			{
				return good;
			}
		}
		return null;
	}

	private string GoodTooltip(MarketPrices.Good good)
	{
		MarketStation market = Market;
		if (market == null)
		{
			return null;
		}
		MarketPrices.Trade standing = MarketOrders.StandingFor(market, good);
		string text = good.Name + ": " + MarketOrders.Stock(market, good) + " in the stockpiles.";
		if (standing != null)
		{
			int keep = MarketOrders.Keep(market, standing);
			text += " Kept at " + ((keep > 0) ? keep.ToString() : "no limit") + ", paid with " + standing.Give.Name + ".";
		}
		return text + " Click to see the ways to pay for it.";
	}

	private string RowTooltip(MarketPrices.Good good, MarketPrices.Trade trade)
	{
		MarketStation market = Market;
		if (market == null)
		{
			return null;
		}
		return trade.GiveAmount + " " + trade.Give.Name + " for " + MarketPrices.Output(trade) + " " + good.Name + " right now (the stockpiles hold " + MarketOrders.Stock(market, trade.Give) + " " + trade.Give.Name + "). Every trade costs a " + Mathf.RoundToInt(MarketPrices.Fee.Value * 100f) + "% fee, and a good gets dearer the more it is traded (back to normal over about " + MarketPrices.RecoveryMinutes.Value.ToString("0.#") + " minutes).";
	}
}
