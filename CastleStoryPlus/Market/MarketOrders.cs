using System;
using System.Collections.Generic;
using Brix.Game;
using Brix.Game.Components;
using Brix.Game.Network;
using CastleStoryPlus.Economy;
using UnityEngine;

namespace CastleStoryPlus.Market;

// The market's standing trades, as the C# menu's market window shows them: every trade in the market's queue is a
// standing trade that keeps a good in stock. The queue loops, and each trade has its own "keep" limit (the loop
// limit per recipe, CraftLoopLimit), so the market only trades for a good while the team's stockpiles hold less
// than its limit and waits once every good has enough. Changing how a good is paid swaps its trade for the trade
// with the other payment and moves the limit with it. All through the game's own crafting commands and the loop
// limit's command, so it works for multiplayer clients and is kept in the save.
internal static class MarketOrders
{
	// Most trades a market queues (the game's crafting queue holds 8).
	internal const int MaxQueue = 8;

	// What a new standing trade keeps in stock.
	internal const int DefaultKeep = 10;

	private static UNetProjectCmd Commands => (User.LocalUser != null) ? User.LocalUser.GetComponent<UNetProjectCmd>() : null;

	internal static Faction FactionOf(MarketStation station)
	{
		return (station != null) ? Faction.GetFaction(station.gameObject) : null;
	}

	// The market's standing trades in queue order (null for a queued recipe that is no trade).
	internal static List<MarketPrices.Trade> Standing(MarketStation station)
	{
		List<MarketPrices.Trade> trades = new List<MarketPrices.Trade>();
		List<RecipeInfo> queue = (station != null) ? station.QueuedRecipes : null;
		if (queue != null)
		{
			foreach (RecipeInfo recipe in queue)
			{
				trades.Add(MarketPrices.TradeFor(recipe));
			}
		}
		return trades;
	}

	// The good's trade in the market's queue, or null.
	internal static MarketPrices.Trade StandingFor(MarketStation station, MarketPrices.Good good)
	{
		return Standing(station).Find((MarketPrices.Trade t) => t != null && t.Get == good);
	}

	// How many of the good the market keeps in stock (0: it trades for it without a limit).
	internal static int Keep(MarketStation station, MarketPrices.Trade trade)
	{
		return (station != null && trade != null) ? CraftLoopLimit.LimitFor(station.state, trade.Recipe.id) : 0;
	}

	// What the team's stockpiles and tool racks hold of the good.
	internal static int Stock(MarketStation station, MarketPrices.Good good)
	{
		Faction faction = FactionOf(station);
		return (faction != null) ? CraftLoopLimit.Stock(faction, good.Resource, cached: true) : 0;
	}

	// Every way the good can be paid for right now.
	internal static List<MarketPrices.Trade> Payments(MarketPrices.Good good)
	{
		List<MarketPrices.Trade> payments = new List<MarketPrices.Trade>();
		foreach (MarketPrices.Good give in MarketPrices.Goods)
		{
			MarketPrices.Trade trade = (give != good) ? MarketPrices.TradeFor(give, good) : null;
			if (trade != null)
			{
				payments.Add(trade);
			}
		}
		return payments;
	}

	// The payment the team can afford most trades of from its stock (the first one when it has none of any).
	internal static MarketPrices.Trade BestPayment(MarketStation station, MarketPrices.Good good)
	{
		MarketPrices.Trade best = null;
		float most = -1f;
		foreach (MarketPrices.Trade trade in Payments(good))
		{
			float trades = (float)Stock(station, trade.Give) / Mathf.Max(1, trade.GiveAmount);
			if (trades > most)
			{
				most = trades;
				best = trade;
			}
		}
		return best;
	}

	// Starts keeping the good in stock: its best payment's trade at the end of the queue, with DefaultKeep, and the
	// queue looping. False when the good already has a trade, has none, or the queue is full.
	internal static bool Add(MarketStation station, MarketPrices.Good good)
	{
		if (station == null || Commands == null || station.QueueCount >= MaxQueue || StandingFor(station, good) != null)
		{
			return false;
		}
		MarketPrices.Trade trade = BestPayment(station, good);
		if (trade == null)
		{
			return false;
		}
		Commands.CallCmdcraftingQueueRecipe(station.gameObject, trade.Recipe.id, 1);
		CraftLoopLimit.Change(station.state, trade.Recipe.id, DefaultKeep);
		if (!station.Looping)
		{
			Commands.CallCmdcraftingLoop(station.gameObject, true);
		}
		return true;
	}

	// Pays the queued trade at index with another good instead: its trade goes to the end of the queue with the same
	// limit.
	internal static bool SetPayment(MarketStation station, int index, MarketPrices.Good give)
	{
		List<MarketPrices.Trade> standing = Standing(station);
		if (Commands == null || index < 0 || index >= standing.Count || standing[index] == null || standing[index].Give == give)
		{
			return false;
		}
		MarketPrices.Trade old = standing[index];
		MarketPrices.Trade trade = MarketPrices.TradeFor(give, old.Get);
		if (trade == null)
		{
			return false;
		}
		int keep = CraftLoopLimit.Raw(station.state, old.Recipe.id);
		Commands.CallCmdcraftingRemoveAt(station.gameObject, index);
		Commands.CallCmdcraftingQueueRecipe(station.gameObject, trade.Recipe.id, 1);
		CraftLoopLimit.Change(station.state, trade.Recipe.id, (keep != 0) ? keep : DefaultKeep);
		CraftLoopLimit.Change(station.state, old.Recipe.id, 0);
		return true;
	}

	// Stops the queued trade at index.
	internal static void Remove(MarketStation station, int index)
	{
		List<MarketPrices.Trade> standing = Standing(station);
		if (Commands == null || index < 0 || index >= standing.Count)
		{
			return;
		}
		Commands.CallCmdcraftingRemoveAt(station.gameObject, index);
		if (standing[index] != null)
		{
			CraftLoopLimit.Change(station.state, standing[index].Recipe.id, 0);
		}
	}

	// What the good's standing trade keeps when it is paid with give; 0 when it has none or is paid otherwise.
	internal static int KeepWith(MarketStation station, MarketPrices.Good good, MarketPrices.Good give)
	{
		MarketPrices.Trade trade = StandingFor(station, good);
		return (trade != null && trade.Give == give) ? Keep(station, trade) : 0;
	}

	// Keeps the good in stock paid with give: its standing trade made, or moved to that payment, with keep as its
	// limit; keep 0 or less stops it. False when there is no such trade or the queue is full.
	internal static bool SetKeep(MarketStation station, MarketPrices.Good good, MarketPrices.Good give, int keep)
	{
		if (station == null || Commands == null)
		{
			return false;
		}
		List<MarketPrices.Trade> standing = Standing(station);
		int index = standing.FindIndex((MarketPrices.Trade t) => t != null && t.Get == good);
		if (keep <= 0)
		{
			if (index >= 0)
			{
				Remove(station, index);
			}
			return true;
		}
		MarketPrices.Trade trade = MarketPrices.TradeFor(give, good);
		if (trade == null)
		{
			return false;
		}
		if (index >= 0 && standing[index].Give != give)
		{
			Commands.CallCmdcraftingRemoveAt(station.gameObject, index);
			CraftLoopLimit.Change(station.state, standing[index].Recipe.id, 0);
			index = -1;
		}
		else if (index < 0 && station.QueueCount >= MaxQueue)
		{
			return false;
		}
		if (index < 0)
		{
			Commands.CallCmdcraftingQueueRecipe(station.gameObject, trade.Recipe.id, 1);
			if (!station.Looping)
			{
				Commands.CallCmdcraftingLoop(station.gameObject, true);
			}
		}
		CraftLoopLimit.Change(station.state, trade.Recipe.id, keep);
		return true;
	}

	// The keep after a step of the counter: from off to DefaultKeep, then as the loop limit steps (1 at a time under
	// 10, 5 under 50, then 10), and from 1 down to off.
	internal static int NextKeep(int keep, int delta)
	{
		if (delta == 0)
		{
			return keep;
		}
		if (keep <= 0)
		{
			return (delta > 0) ? DefaultKeep : 0;
		}
		int direction = Math.Sign(delta);
		for (int i = 0; i < Math.Abs(delta); i++)
		{
			if (keep <= 1 && direction < 0)
			{
				return 0;
			}
			keep = CraftLoopLimit.Next(keep, direction);
		}
		return keep;
	}

	// Raises or lowers what the trade keeps in stock: 1 at a time under 10, 5 under 50, then 10; never below 1. A
	// trade without a limit gets DefaultKeep.
	internal static void StepKeep(MarketStation station, MarketPrices.Trade trade, int delta)
	{
		if (station == null || trade == null || delta == 0)
		{
			return;
		}
		int value = Keep(station, trade);
		if (value <= 0)
		{
			value = DefaultKeep;
		}
		else
		{
			int direction = Math.Sign(delta);
			for (int i = 0; i < Math.Abs(delta); i++)
			{
				value = CraftLoopLimit.Next(value, direction);
			}
		}
		CraftLoopLimit.Change(station.state, trade.Recipe.id, value);
	}
}
