using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;

namespace CastleStoryPlus.Market;

// The trades the player made at a market, to choose again with one click in the market's window: the last RecentMax
// trades queued (newest first) and the ones marked as favourites (kept until taken off, at most FavouriteMax). Both
// lists are kept in the mod's settings ([Market] RecentTrades, FavouriteTrades), so they are the same at every market
// and in every save. A trade is written as "give>get" with each good's asset key, so it survives goods being added or
// renamed; a good that no longer exists is left out.
internal static class MarketMemory
{
	internal const int RecentMax = 6;

	internal const int FavouriteMax = 8;

	internal sealed class Remembered
	{
		public MarketPrices.Good Give;

		public MarketPrices.Good Get;

		public bool Favourite;
	}

	private static ConfigEntry<string> _recent;

	// Counts up with every change, so the window knows to build its list again.
	internal static int Version { get; private set; }

	private static ConfigEntry<string> _favourites;

	private static void Bind()
	{
		if (_recent != null)
		{
			return;
		}
		_recent = Plugin.Cfg.Bind("Market", "RecentTrades", string.Empty, "The last trades queued at a market (\"give>get\" asset keys, newest first), offered again in the market's window.");
		_favourites = Plugin.Cfg.Bind("Market", "FavouriteTrades", string.Empty, "The trades marked as favourites in the market's window (\"give>get\" asset keys).");
	}

	// Favourites first, then the recent trades that are not favourites.
	internal static List<Remembered> All()
	{
		Bind();
		List<Remembered> all = new List<Remembered>();
		foreach (KeyValuePair<MarketPrices.Good, MarketPrices.Good> trade in Parse(_favourites.Value))
		{
			all.Add(new Remembered { Give = trade.Key, Get = trade.Value, Favourite = true });
		}
		foreach (KeyValuePair<MarketPrices.Good, MarketPrices.Good> trade in Parse(_recent.Value))
		{
			if (Find(all, trade.Key, trade.Value) < 0)
			{
				all.Add(new Remembered { Give = trade.Key, Get = trade.Value });
			}
		}
		return all;
	}

	// A trade was queued: it goes to the top of the recent ones.
	internal static void Used(MarketPrices.Good give, MarketPrices.Good get)
	{
		if (give == null || get == null)
		{
			return;
		}
		Bind();
		List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> recent = Parse(_recent.Value);
		Remove(recent, give, get);
		recent.Insert(0, new KeyValuePair<MarketPrices.Good, MarketPrices.Good>(give, get));
		if (recent.Count > RecentMax)
		{
			recent.RemoveRange(RecentMax, recent.Count - RecentMax);
		}
		_recent.Value = Write(recent);
		Version++;
	}

	internal static bool IsFavourite(MarketPrices.Good give, MarketPrices.Good get)
	{
		Bind();
		return Index(Parse(_favourites.Value), give, get) >= 0;
	}

	// Marks the trade as a favourite, or takes the mark off (it stays among the recent ones while it is one).
	internal static void ToggleFavourite(MarketPrices.Good give, MarketPrices.Good get)
	{
		if (give == null || get == null)
		{
			return;
		}
		Bind();
		List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> favourites = Parse(_favourites.Value);
		if (!Remove(favourites, give, get))
		{
			if (favourites.Count >= FavouriteMax)
			{
				favourites.RemoveAt(favourites.Count - 1);
			}
			favourites.Add(new KeyValuePair<MarketPrices.Good, MarketPrices.Good>(give, get));
		}
		_favourites.Value = Write(favourites);
		Version++;
	}

	// Takes the trade off both lists.
	internal static void Forget(MarketPrices.Good give, MarketPrices.Good get)
	{
		Bind();
		List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> favourites = Parse(_favourites.Value);
		if (Remove(favourites, give, get))
		{
			_favourites.Value = Write(favourites);
		}
		List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> recent = Parse(_recent.Value);
		if (Remove(recent, give, get))
		{
			_recent.Value = Write(recent);
		}
		Version++;
	}

	private static int Find(List<Remembered> all, MarketPrices.Good give, MarketPrices.Good get)
	{
		for (int i = 0; i < all.Count; i++)
		{
			if (all[i].Give == give && all[i].Get == get)
			{
				return i;
			}
		}
		return -1;
	}

	private static int Index(List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> trades, MarketPrices.Good give, MarketPrices.Good get)
	{
		for (int i = 0; i < trades.Count; i++)
		{
			if (trades[i].Key == give && trades[i].Value == get)
			{
				return i;
			}
		}
		return -1;
	}

	private static bool Remove(List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> trades, MarketPrices.Good give, MarketPrices.Good get)
	{
		int index = Index(trades, give, get);
		if (index < 0)
		{
			return false;
		}
		trades.RemoveAt(index);
		return true;
	}

	private static List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> Parse(string text)
	{
		List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> trades = new List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>>();
		if (string.IsNullOrEmpty(text))
		{
			return trades;
		}
		foreach (string part in text.Split(','))
		{
			string[] pair = part.Split('>');
			if (pair.Length != 2)
			{
				continue;
			}
			MarketPrices.Good give = GoodOf(pair[0]);
			MarketPrices.Good get = GoodOf(pair[1]);
			if (give != null && get != null && give != get && get.Buyable && Index(trades, give, get) < 0)
			{
				trades.Add(new KeyValuePair<MarketPrices.Good, MarketPrices.Good>(give, get));
			}
		}
		return trades;
	}

	private static string Write(List<KeyValuePair<MarketPrices.Good, MarketPrices.Good>> trades)
	{
		StringBuilder sb = new StringBuilder();
		foreach (KeyValuePair<MarketPrices.Good, MarketPrices.Good> trade in trades)
		{
			if (sb.Length > 0)
			{
				sb.Append(',');
			}
			sb.Append(KeyOf(trade.Key)).Append('>').Append(KeyOf(trade.Value));
		}
		return sb.ToString();
	}

	private static string KeyOf(MarketPrices.Good good)
	{
		return (good.Asset != null) ? good.Asset.Factory + "." + good.Asset.Name : good.Name;
	}

	private static MarketPrices.Good GoodOf(string key)
	{
		foreach (MarketPrices.Good good in MarketPrices.Goods)
		{
			if (KeyOf(good) == key)
			{
				return good;
			}
		}
		return null;
	}
}
