using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.IO.Serialization.Contracts;
using Brix.UI.Icons;
using CastleStoryPlus.Core;
using HarmonyLib;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace CastleStoryPlus.Market;

// Market prices. Every resource has a base value and a heat (1 or more). A trade of A for B gives a fixed lot of B
// and asks as much A as that lot costs right now:
//     in = ceil(out * value(B) * heat(A) * heat(B) / (value(A) * (1 - fee)))
// so the base value that comes out is always less than the base value that goes in, whatever the heat, and any
// chain of trades loses value. A trade is never refused for being too expensive: when prices rise, more is given
// for the same lot (the recipe's ingredient is changed as the prices move, until the trade is made). The base values follow the game's own conversions (3 planks from a log, a rope
// from 10 plant fibres, an iron ingot from 10 raw iron and 2 orange crystals...): a converted resource is never
// worth more than what it is made of, so trading and converting in a circle loses value too.
// Trading a resource heats it up (worse prices for it); the heat falls back to 1 over time (game time). Both
// resources heat up by the base value of the lot that came out, not by what was given: what is given grows with
// the heat, so heating by it made every trade in a queue dearer than the one before, without end. Heat starts at 1
// in every new game and is saved with the world (MarketPricesSave).
[Feature(Features.Market, Features.MarketInfo)]
internal class MarketPrices : MonoBehaviour
{
	internal sealed class Good
	{
		public int Index;

		public Ressource Resource;

		public string Name;

		public float Value;

		public IconKey Icon;

		public Factory.AssetKey Asset;

		public float Heat = 1f;

		// Whether a trade can give it. Steel can only be traded away.
		public bool Buyable = true;
	}

	internal sealed class Trade
	{
		public Good Give;

		public Good Get;

		// What is given and what comes out right now (see Refresh).
		public int GiveAmount;

		public int GetAmount;

		// What comes out at normal prices.
		public int Lot;

		public RecipeInfo Recipe;

		// One of what comes out costs more than the market can hold of what is given, even at normal prices (raw
		// stone for iron or glass): the trade is not offered. Its recipe is still made, so the ids stay the same.
		public bool TooDear;
	}

	// Traded value (in base value units) that doubles the price of a resource.
	private const float HeatValue = 300f;

	// Base value aimed at for one trade, so cheap resources are traded in bigger lots.
	private const float LotValue = 10f;

	// Most of one resource a market holds; also the biggest lot.
	internal const int Capacity = 60;

	// Most a trade asks of a resource: what the market can hold of it (MarketBuilding gives it twice Capacity).
	// When even one of the other resource costs more, one is given for this many (the price stops rising).
	private const int MaxGive = Capacity * 2;

	private const float RefreshSeconds = 0.5f;

	private float _nextRefresh;

	private const int TradeWork = 5;

	internal static ConfigEntry<float> Fee;

	internal static ConfigEntry<float> RecoveryMinutes;

	internal static readonly List<Good> Goods = new List<Good>();

	internal static readonly List<Trade> Trades = new List<Trade>();

	private static readonly Dictionary<int, Trade> TradesByRecipe = new Dictionary<int, Trade>();

	// The goods the market had first: the trades between them are made at start, with the same recipe ids as
	// always, so saved queues keep their trades. Trades with a later good (coal, steel) are made afterwards.
	private static int _firstGoods;

	private static bool _lateDone;

	private static void Enable()
	{
		Fee = Plugin.Cfg.Bind("Market", "Fee", 0.15f, new ConfigDescription("Share of the value lost on every trade.", new AcceptableValueRange<float>(0.05f, 0.9f)));
		RecoveryMinutes = Plugin.Cfg.Bind("Market", "RecoveryMinutes", 5f, "Minutes (game time) for the extra price of a traded resource to fall by half.");
		AddGood(Adjectif.woodBlock, "Log", 3f, IconKeys._Log);
		AddGood(Adjectif.plankBlock, "Plank", 1f, IconKeys._Plank);
		AddGood(Adjectif.stones, "Raw stone", 0.1f, IconKeys._Raw_Stone);
		AddGood(Adjectif.stoneBlock, "Brick", 1f, IconKeys._Brick);
		AddGood(Adjectif.plant, "Plant fibre", 0.5f, IconKeys._Plant);
		AddGood(Adjectif.rope, "Rope", 5f, IconKeys._Rope);
		AddGood(Adjectif.fabric, "Fabric", 5f, IconKeys._Fabric);
		AddGood(Adjectif.rawIron, "Raw iron", 1f, IconKeys._Raw_Iron);
		AddGood(Adjectif.iron, "Iron", 14f, IconKeys._Iron);
		AddGood(Adjectif.cog, "Cog", 14f, IconKeys._Cog);
		AddGood(Adjectif.orangeCrystal, "Orange crystal", 2f, IconKeys._Raw_Orange_Crystal);
		AddGood(Adjectif.blueCrystal, "Blue crystal", 4f, IconKeys._Raw_Blue_Crystal);
		AddGood(Adjectif.glass, "Glass", 20f, IconKeys._Glass_Ingot);
		_firstGoods = Goods.Count;
		// Coal and steel (Metallurgy) are the mod's own items, so their objects are named directly. Values follow
		// their recipes: a log and 2 orange crystals make 4 coal, 1 iron and 4 coal make a steel ingot.
		if (Metallurgy.Metallurgy.IsOn())
		{
			AddGood(Adjectif.clay, "Coal", 1.2f, null, Metallurgy.Metallurgy.CoalKey);
			AddGood(Adjectif.terracotta, "Steel", 18f, null, Metallurgy.Metallurgy.SteelKey).Buyable = false;
		}
		RegisterTrades(0, _firstGoods);
		Plugin.Root.AddComponent<MarketPrices>();
	}

	// Every game starts at normal prices (MarketHeatReset), before a save brings its own (MarketPricesSave).
	internal static void ResetHeat()
	{
		foreach (Good good in Goods)
		{
			good.Heat = 1f;
		}
	}

	// Saves: "Log=1.25,Rope=1.04", the goods whose heat is above 1, by name (names stay the same when goods are added).
	internal static string EncodeHeat()
	{
		StringBuilder text = new StringBuilder();
		foreach (Good good in Goods)
		{
			if (good.Heat <= 1.0001f)
			{
				continue;
			}
			if (text.Length > 0)
			{
				text.Append(',');
			}
			text.Append(good.Name).Append('=').Append(good.Heat.ToString("R", CultureInfo.InvariantCulture));
		}
		return text.ToString();
	}

	internal static void DecodeHeat(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		foreach (string part in text.Split(','))
		{
			int split = part.IndexOf('=');
			if (split <= 0)
			{
				continue;
			}
			string name = part.Substring(0, split);
			if (!float.TryParse(part.Substring(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float heat) || float.IsNaN(heat) || float.IsInfinity(heat))
			{
				continue;
			}
			foreach (Good good in Goods)
			{
				if (good.Name == name)
				{
					good.Heat = Mathf.Max(1f, heat);
				}
			}
		}
	}

	private static Good AddGood(Ressource resource, string name, float value, IconKey icon, Factory.AssetKey asset = null)
	{
		asset = asset ?? Description.RepresentativeOf(resource);
		if (asset == null)
		{
			Plugin.Log.LogWarning("Market: no object for " + name + ", left out");
			return new Good();
		}
		Good good = new Good
		{
			Index = Goods.Count,
			Resource = resource,
			Name = name,
			Value = value,
			Icon = icon,
			Asset = asset
		};
		Goods.Add(good);
		return good;
	}

	// The trades with coal and steel, once the metallurgy and research recipes are made (LateTradesPatch), so every
	// recipe id made before stays as it was. Coal and steel get their icons here, when the icons exist.
	internal static void RegisterLateTrades()
	{
		if (_lateDone || Goods.Count <= _firstGoods)
		{
			return;
		}
		_lateDone = true;
		Metallurgy.Metallurgy.EnsureIcons();
		foreach (Good good in Goods)
		{
			if (good.Icon == null)
			{
				good.Icon = (good.Asset == Metallurgy.Metallurgy.SteelKey) ? Metallurgy.Metallurgy.SteelIcon : Metallurgy.Metallurgy.CoalIcon;
			}
		}
		RegisterTrades(_firstGoods, Goods.Count);
	}

	// One crafting recipe per pair of goods that can be traded, made in the same order on every machine (and kept
	// in the workshops' queues and saves, see WideRecipeIds). Only the pairs that involve a good from first up to
	// (not including) last are made: the first goods' pairs among themselves at start, the later goods' afterwards.
	private static void RegisterTrades(int first, int last)
	{
		if (CookBook.Sword == null)
		{
			return;
		}
		int before = Trades.Count;
		foreach (Good give in Goods)
		{
			foreach (Good get in Goods)
			{
				if (give == get || !get.Buyable || give.Index >= last || get.Index >= last || (give.Index < first && get.Index < first))
				{
					continue;
				}
				RecipeMaker maker = new RecipeMaker
				{
					Work = TradeWork,
					Heat = 0,
					Creates = get.Asset,
					QteCreated = 1,
					DropOnCompletion = false,
					Icon = get.Icon,
					Drawing = give.Icon
				};
				int amount = LotSize(give, get);
				RecipeInfo recipe = maker.With(give.Resource, amount).Make();
				if (recipe.id > Core.WideRecipeIds.MaxId)
				{
					Plugin.Log.LogError("Market: recipe id " + recipe.id + " is too high for the crafting queue, trades left out");
					return;
				}
				int lot = Math.Max(1, (int)Math.Floor(amount * give.Value * (1.0 - Fee.Value) / get.Value + 1e-9));
				Trade trade = new Trade { Give = give, Get = get, GiveAmount = amount, GetAmount = lot, Lot = lot, Recipe = recipe, TooDear = get.Value / (give.Value * (1f - Fee.Value)) > MaxGive };
				Trades.Add(trade);
				TradesByRecipe[recipe.id] = trade;
			}
		}
		Plugin.Log.LogInfo("Market: " + (Trades.Count - before) + " trades registered (" + Trades.Count + " in all)");
	}

	// Enough to get at least one of the other resource at base prices, and about LotValue worth.
	private static int LotSize(Good give, Good get)
	{
		int byValue = Mathf.CeilToInt(LotValue / give.Value);
		int forOne = Mathf.CeilToInt(get.Value / (give.Value * (1f - Fee.Value)) - 0.0001f);
		return Mathf.Clamp(Math.Max(byValue, forOne), 1, Capacity);
	}

	internal static Trade TradeFor(RecipeInfo recipe)
	{
		if (recipe == null)
		{
			return null;
		}
		TradesByRecipe.TryGetValue(recipe.id, out Trade trade);
		return trade;
	}

	internal static Trade TradeFor(Good give, Good get)
	{
		foreach (Trade trade in Trades)
		{
			if (trade.Give == give && trade.Get == get)
			{
				return trade.TooDear ? null : trade;
			}
		}
		return null;
	}

	// What a trade gives right now.
	internal static int Output(Trade trade)
	{
		return trade.GetAmount;
	}

	// How much of the given resource a number of the other costs at the current prices.
	private static int Need(Trade trade, int count)
	{
		double price = (double)count * trade.Get.Value * trade.Give.Heat * trade.Get.Heat;
		double value = (double)trade.Give.Value * (1.0 - Fee.Value);
		return Math.Max(1, (int)Math.Ceiling(price / value - 1e-6));
	}

	// The trade's amounts at the current prices: its lot for what it costs; a smaller lot when the lot would cost
	// more than the market holds, and at least one. True when they changed.
	private static bool Refresh(Trade trade)
	{
		int count = trade.Lot;
		int give = Need(trade, count);
		while (give > MaxGive && count > 1)
		{
			count--;
			give = Need(trade, count);
		}
		give = Math.Min(give, MaxGive);
		if (give == trade.GiveAmount && count == trade.GetAmount)
		{
			return false;
		}
		trade.GiveAmount = give;
		trade.GetAmount = count;
		trade.Recipe.ingredients.Set(trade.Give.Resource, give);
		return true;
	}

	// Server: what a finished trade gives for what was given: the most of the lot it pays for at the current prices,
	// at least one.
	internal static int OutputFor(Trade trade, int given)
	{
		for (int count = trade.Lot; count > 1; count--)
		{
			if (Need(trade, count) <= given)
			{
				return count;
			}
		}
		return 1;
	}

	// Server: a trade finished, both traded resources heat up by the base value that came out.
	internal static void Traded(Trade trade, int output)
	{
		float value = output * trade.Get.Value / HeatValue;
		trade.Give.Heat += value;
		trade.Get.Heat += value;
	}

	// Extra price in percent, e.g. 25 for a heat of 1.25.
	internal static int Markup(Good good)
	{
		return Mathf.RoundToInt((good.Heat - 1f) * 100f);
	}

	private void Update()
	{
		float halfLife = Mathf.Max(0.1f, RecoveryMinutes.Value) * 60f;
		float keep = Mathf.Pow(0.5f, Time.deltaTime / halfLife);
		foreach (Good good in Goods)
		{
			if (good.Heat > 1f)
			{
				good.Heat = 1f + (good.Heat - 1f) * keep;
			}
		}
		if (Time.unscaledTime < _nextRefresh)
		{
			return;
		}
		_nextRefresh = Time.unscaledTime + RefreshSeconds;
		bool changed = false;
		foreach (Trade trade in Trades)
		{
			changed |= Refresh(trade);
		}
		if (changed)
		{
			RefreshStations();
		}
	}

	// The markets fetch what their trades ask now: the order being worked on asks the new amount too, until it is
	// made (its ingredients are a copy taken when it started).
	private static void RefreshStations()
	{
		foreach (MarketStation station in Live<MarketStation>.Active())
		{
			Recipe current = station.CurrentRecipe;
			Trade trade = (current != null && !current.Finished) ? TradeFor(current.Info) : null;
			if (trade != null)
			{
				current.SetIngredient(trade.Give.Resource, trade.GiveAmount);
			}
			station.RefeshInventory();
		}
	}
}

// Normal prices when a game starts loading, before its saved objects (and the saved prices) load; prices of the
// last game played in the session do not carry over.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(GameLoader), nameof(GameLoader.LoadNewGameFromGameParams))]
internal static class MarketHeatReset
{
	private static void Prefix()
	{
		MarketPrices.ResetHeat();
	}
}

// Saves the market prices (the heat of every good) as an extra property on the existing Faction component
// ("marketHeat": "Log=1.25,..."). The prices are the same for every team, so every team saves the same text and
// loading any of them gives the prices back. The unmodded game ignores unknown properties.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(DefaultGameContractResolver), nameof(DefaultGameContractResolver.GenerateDefaultMonoBehaviourContract))]
internal static class MarketPricesSave
{
	private const string Property = "marketHeat";

	private class HeatProvider : IValueProvider
	{
		public object GetValue(object target)
		{
			return MarketPrices.EncodeHeat();
		}

		public void SetValue(object target, object value)
		{
			MarketPrices.DecodeHeat(Convert.ToString(value));
		}
	}

	private static void Postfix(Type objectType, JsonObjectContract __result)
	{
		if (objectType != typeof(Faction) || __result == null || __result.Properties.Contains(Property))
		{
			return;
		}
		__result.Properties.AddProperty(new JsonProperty
		{
			PropertyName = Property,
			UnderlyingName = Property,
			PropertyType = typeof(string),
			DeclaringType = typeof(Faction),
			ValueProvider = new HeatProvider(),
			Readable = true,
			Writable = true
		});
	}
}
