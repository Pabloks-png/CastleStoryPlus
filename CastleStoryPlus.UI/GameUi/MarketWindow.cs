using System.Collections.Generic;
using System.Text;
using Brix.Game.Components;
using CastleStoryPlus.LuaUi;
using CastleStoryPlus.Market;
using CastleStoryPlus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.GameUi;

// The market's window: trading goods for other goods. "Give" and "Get" are the two goods (a click lists every good
// with its name and what the stockpiles hold; "Swap" turns them round), under them the price right now ("3 Orange
// crystal -> 2 Rope"), how many times to trade ("-  1  +", with what that gives and gets in all) and "Trade", which
// queues the trades. The queue is listed below, the same trades one after another as one row ("x 4") with "x" to take
// them out. Every trade is made once: the market's queue does not loop (MarketOrders.Barter). Over the goods, the trades
// made before and the favourites (MarketMemory): a click chooses that trade's goods, "Fav" keeps it in the list (at the
// top) and "x" takes it out.
internal class CsMarketWindow : CsStationWindow
{
	private enum Side
	{
		None,
		Give,
		Get
	}

	private sealed class GoodPill
	{
		public Image Icon;

		public Text Name;

		public Text Have;

		public Text Change;
	}

	private sealed class GoodCell
	{
		public MarketPrices.Good Good;

		public Text Have;
	}

	private sealed class QueueRow
	{
		public int Index;

		public int Count;

		public Image Background;

		public Text Status;
	}

	private sealed class SavedRow
	{
		public MarketMemory.Remembered Trade;

		public Image Background;
	}

	private static readonly Color Bad = new Color(0.9f, 0.36f, 0.26f, 1f);

	private static readonly Color Trading = new Color(1f, 197f / 255f, 10f / 255f, 0.14f);

	private readonly List<GoodCell> _cells = new List<GoodCell>();

	private readonly List<QueueRow> _queueRows = new List<QueueRow>();

	private readonly List<SavedRow> _savedRows = new List<SavedRow>();

	private RectTransform _saved;

	private MarketPrices.Good _give;

	private MarketPrices.Good _get;

	private int _times = 1;

	// Which good's list is open.
	private Side _picking;

	private GoodPill _givePill;

	private GoodPill _getPill;

	private RectTransform _giveList;

	private RectTransform _getList;

	private Image _priceGiveIcon;

	private Text _priceGive;

	private Image _priceGetIcon;

	private Text _priceGet;

	private UiCounter _counter;

	private Text _total;

	private Button _trade;

	private Text _tradeText;

	private Text _queueCount;

	private RectTransform _queue;

	private Text _queueEmpty;

	private GameObject _looping;

	private string _built;

	protected override float Width
	{
		get { return 430f; }
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
		_picking = Side.None;
		_times = 1;
		_built = null;
	}

	private MarketStation Market
	{
		get { return Station as MarketStation; }
	}

	// ---- build

	protected override void Build()
	{
		Caption(Body, "Trade");
		_saved = Column(Body, 4f);
		_givePill = BuildPill(Body, "Give", Side.Give);
		_giveList = Column(Body, 4f);
		Transform swapRow = Row(Body, 22f, 6f);
		RectTransform spacer = UiKit.CreateRect("Spacer", swapRow);
		UiKit.Fixed(spacer.gameObject, 38f, 22f);
		Button swap = UiKit.CreateButton(swapRow, "Swap", 60f, () =>
		{
			MarketPrices.Good give = _give;
			_give = _get;
			_get = give;
			_picking = Side.None;
			Soon();
		});
		UiKit.Fixed(swap.gameObject, 60f, 22f);
		RoundButton(swap, UiTheme.SlotRadius);
		SetButtonText(swap, LuiStyle.SmallFontSize + 1, UiTheme.TextMuted);
		Clickable(swap.gameObject, null, () => "Turn the trade round: give what you would get.");
		_getPill = BuildPill(Body, "Get", Side.Get);
		_getList = Column(Body, 4f);

		// The price right now.
		Transform price = Row(Body, 36f, 8f);
		HorizontalLayoutGroup priceLayout = price.GetComponent<HorizontalLayoutGroup>();
		priceLayout.childAlignment = TextAnchor.MiddleCenter;
		priceLayout.padding = new RectOffset(8, 8, 4, 4);
		UiTheme.Round(price.gameObject.AddComponent<Image>(), UiTheme.BgBlock, UiTheme.SlotRadius);
		_priceGiveIcon = Picture(price, 26f);
		_priceGive = LuiWidgets.Label(price, string.Empty, LuiStyle.FontSize + 2, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		_priceGive.supportRichText = true;
		Text arrow = LuiWidgets.Label(price, "→", LuiStyle.FontSize + 4, LuiStyle.CastleYellow, TextAnchor.MiddleCenter, LuiStyle.Bold);
		UiKit.Fixed(arrow.gameObject, 24f, 28f);
		_priceGetIcon = Picture(price, 26f);
		_priceGet = LuiWidgets.Label(price, string.Empty, LuiStyle.FontSize + 2, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		Clickable(price.gameObject, null, PriceTooltip);

		// How many times, and the button.
		Transform times = Row(Body, 28f, 8f);
		Text timesLabel = LuiWidgets.Label(times, "Times", LuiStyle.SmallFontSize + 1, UiTheme.TextMuted, TextAnchor.MiddleLeft, LuiStyle.Regular);
		UiKit.Fixed(timesLabel.gameObject, 38f, 24f);
		_counter = UiCounter.Create(times, 92f, (int delta) =>
		{
			_times = Mathf.Clamp(_times + delta, 1, MarketOrders.MaxQueue);
			Soon();
		});
		Clickable(_counter.gameObject, null, () => "How many times to make the trade (one queue slot each, " + MarketOrders.MaxQueue + " at most).");
		_total = LuiWidgets.Label(times, string.Empty, LuiStyle.SmallFontSize + 1, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Regular);
		_total.supportRichText = true;
		UiKit.Flexible(_total.gameObject);
		_trade = UiKit.CreateButton(Body, "Trade", Width - 24f, Queue);
		UiKit.Fixed(_trade.gameObject, Width - 24f, 32f);
		RoundButton(_trade, UiTheme.ButtonRadius);
		ColorBlock colors = _trade.colors;
		colors.normalColor = LuiStyle.CastleYellow;
		colors.highlightedColor = new Color(1f, 0.85f, 0.35f, 1f);
		colors.pressedColor = new Color(0.85f, 0.65f, 0.05f, 1f);
		_trade.colors = colors;
		_tradeText = _trade.GetComponentInChildren<Text>();
		_tradeText.color = UiTheme.TextOnYellow;
		_tradeText.fontSize = LuiStyle.FontSize + 1;

		// The queue.
		Line(Body);
		Transform head = Row(Body, 18f, 6f);
		Text caption = Caption(head, "Queue");
		UiKit.Flexible(caption.gameObject);
		_queueCount = LuiWidgets.Label(head, string.Empty, LuiStyle.SmallFontSize, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Bold);
		UiKit.Fixed(_queueCount.gameObject, 80f, 18f);
		_queueEmpty = Note(Body, "Nothing queued. Choose what to give and what to get, then Trade.");
		_queue = Column(Body, 4f);
		Transform looping = Row(Body, 26f, 6f);
		_looping = looping.gameObject;
		Text loopText = LuiWidgets.Label(looping, "This queue repeats (standing trades from an older save).", LuiStyle.SmallFontSize, Bad, TextAnchor.MiddleLeft, LuiStyle.Regular);
		loopText.horizontalOverflow = HorizontalWrapMode.Wrap;
		UiKit.Flexible(loopText.gameObject);
		Button stop = UiKit.CreateButton(looping, "Stop", 52f, () =>
		{
			MarketOrders.StopLooping(Market);
			Soon();
		});
		UiKit.Fixed(stop.gameObject, 52f, 24f);
		RoundButton(stop, UiTheme.SlotRadius);
		SetButtonText(stop, LuiStyle.SmallFontSize + 1, LuiStyle.White);
		_looping.SetActive(false);
	}

	private GoodPill BuildPill(Transform parent, string label, Side side)
	{
		GoodPill view = new GoodPill();
		Transform row = Row(parent, 34f, 6f);
		Text caption = LuiWidgets.Label(row, label, LuiStyle.FontSize, UiTheme.TextMuted, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Fixed(caption.gameObject, 38f, 30f);
		RectTransform pill = UiKit.CreateRect("Good", row);
		HorizontalLayoutGroup layout = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(6, 10, 3, 3);
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.MiddleLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = false;
		UiKit.Flexible(pill.gameObject);
		UiTheme.Round(pill.gameObject.AddComponent<Image>(), UiTheme.BgSlot, UiTheme.SlotRadius);
		view.Icon = Picture(pill, 26f);
		view.Name = LuiWidgets.Label(pill, string.Empty, LuiStyle.FontSize + 1, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Flexible(view.Name.gameObject);
		view.Have = LuiWidgets.Label(pill, string.Empty, LuiStyle.SmallFontSize + 1, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Regular);
		view.Have.supportRichText = true;
		UiKit.Fixed(view.Have.gameObject, 80f, 24f);
		view.Change = LuiWidgets.Label(pill, "change", LuiStyle.SmallFontSize, LuiStyle.CastleYellow, TextAnchor.MiddleRight, LuiStyle.Bold);
		UiKit.Fixed(view.Change.gameObject, 46f, 24f);
		Clickable(pill.gameObject, () =>
		{
			_picking = (_picking == side) ? Side.None : side;
			Soon();
		}, () => (side == Side.Give) ? "What the market takes from the stockpiles. Click to choose." : "What the market gives back. Click to choose.");
		return view;
	}

	private void Rebuild(MarketStation market, List<MarketPrices.Trade> queue, string signature)
	{
		_built = signature;
		Clear(_giveList);
		Clear(_getList);
		Clear(_queue);
		Clear(_saved);
		_cells.Clear();
		_queueRows.Clear();
		_savedRows.Clear();
		foreach (MarketMemory.Remembered remembered in MarketMemory.All())
		{
			_savedRows.Add(BuildSavedRow(remembered));
		}
		if (_picking == Side.Give)
		{
			BuildGoods(_giveList, Side.Give);
		}
		else if (_picking == Side.Get)
		{
			BuildGoods(_getList, Side.Get);
		}
		for (int i = 0; i < queue.Count;)
		{
			int count = 1;
			while (i + count < queue.Count && SameTrade(queue[i], queue[i + count]))
			{
				count++;
			}
			_queueRows.Add(BuildQueueRow(queue[i], i, count));
			i += count;
		}
		LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
	}

	private static bool SameTrade(MarketPrices.Trade a, MarketPrices.Trade b)
	{
		return a != null && b != null && a.Give == b.Give && a.Get == b.Get;
	}

	private static void Clear(Transform parent)
	{
		List<GameObject> old = new List<GameObject>();
		foreach (Transform child in parent)
		{
			old.Add(child.gameObject);
		}
		foreach (GameObject go in old)
		{
			DestroyImmediate(go);
		}
	}

	// Every good that can go on that side, two to a row, with its name and stock.
	private void BuildGoods(Transform parent, Side side)
	{
		float cell = Mathf.Floor((Width - 24f - 6f) / 2f);
		RectTransform grid = Grid(parent, new Vector2(cell, 30f), 6f, 2);
		foreach (MarketPrices.Good good in MarketPrices.Goods)
		{
			if (side == Side.Get && !good.Buyable)
			{
				continue;
			}
			bool chosen = good == ((side == Side.Give) ? _give : _get);
			RectTransform rect = UiKit.CreateRect("Good", grid);
			HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
			layout.padding = new RectOffset(6, 8, 3, 3);
			layout.spacing = 6f;
			layout.childAlignment = TextAnchor.MiddleLeft;
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = false;
			layout.childForceExpandHeight = false;
			UiTheme.Round(rect.gameObject.AddComponent<Image>(), chosen ? UiTheme.BgSelectedRow : UiTheme.BgSlot, UiTheme.SlotRadius);
			Image icon = Picture(rect, 22f);
			SetIcon(icon, good);
			Text name = LuiWidgets.Label(rect, good.Name, LuiStyle.FontSize, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
			UiKit.Flexible(name.gameObject);
			GoodCell view = new GoodCell { Good = good };
			view.Have = LuiWidgets.Label(rect, string.Empty, LuiStyle.SmallFontSize + 1, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Regular);
			UiKit.Fixed(view.Have.gameObject, 44f, 24f);
			Clickable(rect.gameObject, () =>
			{
				if (side == Side.Give)
				{
					if (_get == good)
					{
						_get = _give;
					}
					_give = good;
				}
				else
				{
					if (_give == good)
					{
						_give = _get;
					}
					_get = good;
				}
				_picking = Side.None;
				Soon();
			}, () => good.Name + ": the stockpiles hold " + MarketOrders.Stock(Market, good) + ".");
			_cells.Add(view);
		}
	}

	// A trade made before or a favourite: a click chooses its goods.
	private SavedRow BuildSavedRow(MarketMemory.Remembered remembered)
	{
		SavedRow view = new SavedRow { Trade = remembered };
		Transform row = Row(_saved, 28f, 6f);
		HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(4, 4, 2, 2);
		view.Background = row.gameObject.AddComponent<Image>();
		UiTheme.Round(view.Background, UiTheme.BgSlot, UiTheme.SlotRadius);
		Button favourite = UiKit.CreateButton(row, "Fav", 38f, () =>
		{
			MarketMemory.ToggleFavourite(remembered.Give, remembered.Get);
			Soon();
		});
		UiKit.Fixed(favourite.gameObject, 38f, 22f);
		RoundButton(favourite, UiTheme.SlotRadius);
		SetButtonText(favourite, LuiStyle.SmallFontSize, remembered.Favourite ? LuiStyle.CastleYellow : UiTheme.TextMuted);
		Clickable(favourite.gameObject, null, () => remembered.Favourite ? "A favourite: it stays in this list. Click to take the mark off." : "Make it a favourite: it stays at the top of this list.");
		Image give = Picture(row, 20f);
		SetIcon(give, remembered.Give);
		Text giveText = LuiWidgets.Label(row, remembered.Give.Name, LuiStyle.SmallFontSize + 1, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		Text arrow = LuiWidgets.Label(row, "→", LuiStyle.SmallFontSize + 1, LuiStyle.CastleYellow, TextAnchor.MiddleCenter, LuiStyle.Bold);
		UiKit.Fixed(arrow.gameObject, 14f, 22f);
		Image get = Picture(row, 20f);
		SetIcon(get, remembered.Get);
		Text getText = LuiWidgets.Label(row, remembered.Get.Name, LuiStyle.SmallFontSize + 1, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
		UiKit.Flexible(getText.gameObject);
		giveText.horizontalOverflow = getText.horizontalOverflow = HorizontalWrapMode.Overflow;
		Button forget = UiKit.CreateButton(row, "x", 22f, () =>
		{
			MarketMemory.Forget(remembered.Give, remembered.Get);
			Soon();
		});
		UiKit.Fixed(forget.gameObject, 22f, 22f);
		RoundButton(forget, UiTheme.SlotRadius);
		SetButtonText(forget, LuiStyle.FontSize, UiTheme.TextMuted);
		Clickable(forget.gameObject, null, () => "Take this trade out of the list.");
		Clickable(row.gameObject, () =>
		{
			_give = remembered.Give;
			_get = remembered.Get;
			_picking = Side.None;
			Soon();
		}, () => "Choose this trade: give " + remembered.Give.Name + ", get " + remembered.Get.Name + ".");
		return view;
	}

	private QueueRow BuildQueueRow(MarketPrices.Trade trade, int index, int count)
	{
		QueueRow view = new QueueRow { Index = index, Count = count };
		Transform row = Row(_queue, 30f, 6f);
		HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(8, 4, 2, 2);
		view.Background = row.gameObject.AddComponent<Image>();
		UiTheme.Round(view.Background, UiTheme.BgBlock, UiTheme.SlotRadius);
		if (trade == null)
		{
			Text other = LuiWidgets.Label(row, "Something that is no trade", LuiStyle.FontSize, UiTheme.TextMuted, TextAnchor.MiddleLeft, LuiStyle.Regular);
			UiKit.Flexible(other.gameObject);
		}
		else
		{
			Image give = Picture(row, 22f);
			SetIcon(give, trade.Give);
			Text giveText = LuiWidgets.Label(row, trade.Give.Name, LuiStyle.FontSize, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
			Text arrow = LuiWidgets.Label(row, "→", LuiStyle.FontSize, LuiStyle.CastleYellow, TextAnchor.MiddleCenter, LuiStyle.Bold);
			UiKit.Fixed(arrow.gameObject, 16f, 24f);
			Image get = Picture(row, 22f);
			SetIcon(get, trade.Get);
			Text getText = LuiWidgets.Label(row, trade.Get.Name, LuiStyle.FontSize, LuiStyle.White, TextAnchor.MiddleLeft, LuiStyle.Bold);
			UiKit.Flexible(getText.gameObject);
			giveText.horizontalOverflow = getText.horizontalOverflow = HorizontalWrapMode.Overflow;
		}
		Text times = LuiWidgets.Label(row, (count > 1) ? "× " + count : string.Empty, LuiStyle.FontSize, LuiStyle.White, TextAnchor.MiddleRight, LuiStyle.Bold);
		UiKit.Fixed(times.gameObject, 34f, 24f);
		view.Status = LuiWidgets.Label(row, string.Empty, LuiStyle.SmallFontSize + 1, UiTheme.TextMuted, TextAnchor.MiddleRight, LuiStyle.Bold);
		UiKit.Fixed(view.Status.gameObject, 76f, 24f);
		Button remove = UiKit.CreateButton(row, "x", 24f, () =>
		{
			MarketOrders.RemoveRange(Market, index, count);
			Soon();
		});
		UiKit.Fixed(remove.gameObject, 24f, 24f);
		RoundButton(remove, UiTheme.SlotRadius);
		SetButtonText(remove, LuiStyle.FontSize, UiTheme.TextMuted);
		Clickable(remove.gameObject, null, () => (count > 1) ? "Take these " + count + " trades out of the queue." : "Take this trade out of the queue.");
		return view;
	}

	private void Queue()
	{
		if (MarketOrders.Barter(Market, _give, _get, _times) > 0)
		{
			MarketMemory.Used(_give, _get);
			_times = 1;
			_picking = Side.None;
			Soon();
		}
	}

	// ---- read

	protected override void Read(CraftingStation station)
	{
		MarketStation market = (MarketStation)station;
		Choose(market);
		List<MarketPrices.Trade> queue = MarketOrders.Standing(market);
		string signature = Signature(queue);
		if (signature != _built)
		{
			Rebuild(market, queue, signature);
		}

		MarketPrices.Trade current = (market.CurrentRecipe != null) ? MarketPrices.TradeFor(market.CurrentRecipe.Info) : null;
		SetSubtitle((current != null) ? "Trading " + current.Give.Name + " for " + current.Get.Name : (queue.Count > 0) ? "Waiting for a worker" : "Nothing to trade");

		ReadPill(_givePill, market, _give, Side.Give);
		ReadPill(_getPill, market, _get, Side.Get);
		MarketPrices.Trade trade = (_give != null && _get != null) ? MarketPrices.TradeFor(_give, _get) : null;
		int free = MarketOrders.MaxQueue - market.QueueCount;
		_times = Mathf.Clamp(_times, 1, Mathf.Max(1, free));
		_counter.Set(_times.ToString());
		SetIcon(_priceGiveIcon, (trade != null) ? _give : null);
		SetIcon(_priceGetIcon, (trade != null) ? _get : null);
		if (trade != null)
		{
			int stock = MarketOrders.Stock(market, _give);
			bool lacking = stock < trade.GiveAmount * _times;
			CsRowParts.SetText(_priceGive, trade.GiveAmount + " " + _give.Name);
			CsRowParts.SetText(_priceGet, MarketPrices.Output(trade) + " " + _get.Name);
			CsRowParts.SetText(_total, "in all <color=" + UiTheme.Hex(lacking ? Bad : LuiStyle.White) + "><b>" + trade.GiveAmount * _times + "</b></color> → <b>" + MarketPrices.Output(trade) * _times + "</b>");
		}
		else
		{
			CsRowParts.SetText(_priceGive, (_give == null || _get == null) ? "Choose two goods" : "No trade between these");
			CsRowParts.SetText(_priceGet, string.Empty);
			CsRowParts.SetText(_total, string.Empty);
		}
		_trade.interactable = trade != null && free > 0;
		CsRowParts.SetText(_tradeText, (free <= 0) ? "The queue is full" : (_times > 1) ? "Trade " + _times + " times" : "Trade");

		CsRowParts.SetText(_queueCount, queue.Count + " / " + MarketOrders.MaxQueue);
		_queueEmpty.gameObject.SetActive(queue.Count == 0);
		if (_looping.activeSelf != market.Looping)
		{
			_looping.SetActive(market.Looping);
			LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
		}
		foreach (QueueRow row in _queueRows)
		{
			MarketPrices.Trade queued = (row.Index < queue.Count) ? queue[row.Index] : null;
			bool first = row.Index == 0 && current != null;
			bool cantPay = queued != null && MarketOrders.Stock(market, queued.Give) < queued.GiveAmount;
			CsRowParts.SetText(row.Status, first ? "Trading..." : cantPay ? "Can't pay" : "Waiting");
			row.Status.color = first ? LuiStyle.CastleYellow : cantPay ? Bad : UiTheme.TextMuted;
			row.Background.color = first ? Trading : UiTheme.BgBlock;
		}
		foreach (GoodCell cell in _cells)
		{
			CsRowParts.SetText(cell.Have, MarketOrders.Stock(market, cell.Good).ToString());
		}
		foreach (SavedRow row in _savedRows)
		{
			bool chosen = row.Trade.Give == _give && row.Trade.Get == _get;
			row.Background.color = chosen ? UiTheme.BgSelectedRow : UiTheme.BgSlot;
		}
	}

	// Two goods chosen at first: the first buyable good, paid the way the team can pay most often.
	private void Choose(MarketStation market)
	{
		if (_get == null)
		{
			foreach (MarketPrices.Good good in MarketPrices.Goods)
			{
				if (good.Buyable)
				{
					_get = good;
					break;
				}
			}
		}
		if (_give == null && _get != null)
		{
			MarketPrices.Trade best = MarketOrders.BestPayment(market, _get);
			_give = (best != null) ? best.Give : null;
		}
	}

	private void ReadPill(GoodPill pill, MarketStation market, MarketPrices.Good good, Side side)
	{
		SetIcon(pill.Icon, good);
		CsRowParts.SetText(pill.Name, (good != null) ? good.Name : "Choose");
		CsRowParts.SetText(pill.Have, (good != null) ? "have <b>" + MarketOrders.Stock(market, good) + "</b>" : string.Empty);
		CsRowParts.SetText(pill.Change, (_picking == side) ? "close" : "change");
	}

	// What the window is built for: the queue, and which list of goods is open with what is chosen.
	private string Signature(List<MarketPrices.Trade> queue)
	{
		StringBuilder sb = new StringBuilder();
		foreach (MarketPrices.Trade trade in queue)
		{
			sb.Append((trade != null) ? trade.Give.Index + ">" + trade.Get.Index : "-").Append(',');
		}
		sb.Append('|').Append(MarketMemory.Version).Append('|').Append(_picking).Append('|').Append((_give != null) ? _give.Index : -1).Append('|').Append((_get != null) ? _get.Index : -1);
		return sb.ToString();
	}

	private static void SetIcon(Image image, MarketPrices.Good good)
	{
		Sprite sprite = (good != null && good.Icon != null) ? good.Icon.Get64() : null;
		image.sprite = sprite;
		image.enabled = sprite != null;
	}

	private static void SetButtonText(Button button, int size, Color color)
	{
		Text text = button.GetComponentInChildren<Text>();
		if (text != null)
		{
			text.fontSize = size;
			text.color = color;
		}
	}

	private string PriceTooltip()
	{
		MarketStation market = Market;
		MarketPrices.Trade trade = (market != null && _give != null && _get != null) ? MarketPrices.TradeFor(_give, _get) : null;
		if (trade == null)
		{
			return null;
		}
		return trade.GiveAmount + " " + _give.Name + " for " + MarketPrices.Output(trade) + " " + _get.Name + " right now (the stockpiles hold " + MarketOrders.Stock(market, _give) + " " + _give.Name + "). Every trade costs a " + Mathf.RoundToInt(MarketPrices.Fee.Value * 100f) + "% fee, and a good gets dearer the more it is traded (back to normal over about " + MarketPrices.RecoveryMinutes.Value.ToString("0.#") + " minutes), so trades further down the queue can cost a little more.";
	}
}
