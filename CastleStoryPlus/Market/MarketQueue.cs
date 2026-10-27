using System;
using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Network;
using Brix.Network;
using Brix.IO.Serialization.Contracts;
using CastleStoryPlus.Core;
using HarmonyLib;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Market;

// A market queues 12 trades instead of the game's 8. The game keeps a crafting queue as 8 recipe ids of a byte each
// in one number (CraftingState.recipes, synced and saved), and the station's list of queued recipes is read back from
// it. A market's 9th to 12th trade are kept beside it in the same way (Extra), read back after the first 8, counted in
// the number the station compares to see that its queue changed (CraftingState.Hash), saved with the crafting state
// ("plusExtraRecipes", which the game without the mod ignores: the market then keeps its first 8) and sent by the host
// to the other players (message ExtraMessage) when they change and every few seconds for players who join. Other
// workshops keep 8.
[Feature(Features.Market, Features.MarketInfo)]
internal sealed class MarketQueue : MonoBehaviour
{
	internal const int Slots = 12;

	// What the game's own number holds.
	private const int Main = 8;

	private const short ExtraMessage = 201;

	private const float ResendSeconds = 5f;

	internal const string Property = "plusExtraRecipes";

	private static readonly Dictionary<CraftingState, ulong> Extra = new Dictionary<CraftingState, ulong>();

	private static bool _changed;

	private float _nextSend;

	private sealed class ExtrasMessage : MessageBase
	{
		public readonly List<NetworkInstanceId> States = new List<NetworkInstanceId>();

		public readonly List<ulong> Values = new List<ulong>();

		public override void Serialize(NetworkWriter writer)
		{
			writer.Write(States.Count);
			for (int i = 0; i < States.Count; i++)
			{
				writer.Write(States[i]);
				writer.Write(Values[i]);
			}
		}

		public override void Deserialize(NetworkReader reader)
		{
			int count = reader.ReadInt32();
			for (int i = 0; i < count; i++)
			{
				States.Add(reader.ReadNetworkId());
				Values.Add(reader.ReadUInt64());
			}
		}
	}

	private static void Enable()
	{
		GameSession.OnLevelReady(LevelReady);
		GameSession.OnLeave(() =>
		{
			Extra.Clear();
			_changed = false;
		});
		Plugin.Root.AddComponent<MarketQueue>();
	}

	private static void LevelReady()
	{
		NetworkClient client = UNetManager.instance?.LocalNetworkClient;
		if (client != null)
		{
			if (client.handlers.ContainsKey(ExtraMessage))
			{
				client.UnregisterHandler(ExtraMessage);
			}
			client.RegisterHandler(ExtraMessage, Received);
		}
	}

	internal static bool IsMarket(Component component)
	{
		return component != null && component.GetComponent<MarketStation>() != null;
	}

	internal static ulong ExtraOf(CraftingState state)
	{
		return (state != null && Extra.TryGetValue(state, out ulong value)) ? value : 0uL;
	}

	internal static void SetExtra(CraftingState state, ulong value)
	{
		if (state == null || ExtraOf(state) == value)
		{
			return;
		}
		if (value == 0uL)
		{
			Extra.Remove(state);
		}
		else
		{
			Extra[state] = value;
		}
		_changed = true;
	}

	// The 9th to 12th recipe of a market's queue, packed as the game packs the first 8.
	internal static ulong Pack(List<RecipeInfo> recipes)
	{
		int[] ids = new int[Main];
		for (int i = Main; i < Mathf.Min(Slots, recipes.Count); i++)
		{
			ids[i - Main] = recipes[i].id;
		}
		return CraftingState.ToLong(ids);
	}

	internal static void Unpack(ulong value, List<RecipeInfo> results)
	{
		int[] ids = new int[Main];
		CraftingState.FromLong(value, ids);
		for (int i = 0; i < Slots - Main; i++)
		{
			if (ids[i] != 0)
			{
				results.Add(CookBook.GetById(ids[i]));
			}
		}
	}

	private void Update()
	{
		if (!NetworkServer.active || (!_changed && Time.unscaledTime < _nextSend))
		{
			return;
		}
		_changed = false;
		_nextSend = Time.unscaledTime + ResendSeconds;
		ExtrasMessage message = new ExtrasMessage();
		foreach (MarketStation station in Live<MarketStation>.Active())
		{
			CraftingState state = (station != null) ? station.state : null;
			if (state == null)
			{
				continue;
			}
			message.States.Add(state.netId);
			message.Values.Add(ExtraOf(state));
		}
		if (message.States.Count > 0)
		{
			try
			{
				NetworkServer.SendToAll(ExtraMessage, message);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("MarketQueue: " + ex.Message);
			}
		}
	}

	private static void Received(NetworkMessage network)
	{
		if (NetworkServer.active)
		{
			return;
		}
		ExtrasMessage message = network.ReadMessage<ExtrasMessage>();
		for (int i = 0; i < message.States.Count; i++)
		{
			GameObject go = ClientScene.FindLocalObject(message.States[i]);
			CraftingState state = (go != null) ? go.GetComponent<CraftingState>() : null;
			if (state != null)
			{
				SetExtra(state, message.Values[i]);
			}
		}
	}
}

// Queuing at a market: up to 12.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(CraftingLabor), nameof(CraftingLabor.QueueRecipe))]
internal static class MarketQueueLimitPatch
{
	private static bool Prefix(CraftingLabor __instance, RecipeInfo recipe, int qty)
	{
		if (!MarketQueue.IsMarket(__instance))
		{
			return true;
		}
		for (int i = 0; i < qty; i++)
		{
			if (__instance.QueueCount() >= MarketQueue.Slots)
			{
				break;
			}
			__instance.QueuedRecipes.Add(recipe);
		}
		__instance.state.SetRecipes(__instance.QueuedRecipes);
		return false;
	}
}

// Storing a market's queue: the game's number takes the first 8, Extra the rest.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(CraftingState), nameof(CraftingState.SetRecipes))]
internal static class MarketQueueStorePatch
{
	private static void Prefix(CraftingState __instance, List<RecipeInfo> recipes)
	{
		if (recipes != null && MarketQueue.IsMarket(__instance))
		{
			MarketQueue.SetExtra(__instance, MarketQueue.Pack(recipes));
		}
	}
}

[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(CraftingState), nameof(CraftingState.GetRecipe))]
internal static class MarketQueueReadPatch
{
	private static void Postfix(CraftingState __instance, List<RecipeInfo> results)
	{
		ulong extra = MarketQueue.ExtraOf(__instance);
		if (extra != 0uL && results != null)
		{
			MarketQueue.Unpack(extra, results);
		}
	}
}

// The station reads its queue again when this number changes: a change among the extra trades changes it too.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(CraftingState), nameof(CraftingState.Hash), MethodType.Getter)]
internal static class MarketQueueHashPatch
{
	private static void Postfix(CraftingState __instance, ref ulong __result)
	{
		ulong extra = MarketQueue.ExtraOf(__instance);
		if (extra != 0uL)
		{
			__result ^= unchecked(extra * 0x9E3779B97F4A7C15uL);
		}
	}
}

// Saves: an extra "plusExtraRecipes" property on the crafting state.
[Feature(Features.Market, Features.MarketInfo)]
[HarmonyPatch(typeof(DefaultGameContractResolver), nameof(DefaultGameContractResolver.GenerateDefaultMonoBehaviourContract))]
internal static class MarketQueueSave
{
	private class ExtraProvider : IValueProvider
	{
		public object GetValue(object target)
		{
			return MarketQueue.ExtraOf(target as CraftingState);
		}

		public void SetValue(object target, object value)
		{
			MarketQueue.SetExtra(target as CraftingState, Convert.ToUInt64(value));
		}
	}

	private static void Postfix(Type objectType, JsonObjectContract __result)
	{
		if (objectType != typeof(CraftingState) || __result == null || __result.Properties.Contains(MarketQueue.Property))
		{
			return;
		}
		__result.Properties.AddProperty(new JsonProperty
		{
			PropertyName = MarketQueue.Property,
			UnderlyingName = MarketQueue.Property,
			PropertyType = typeof(ulong),
			DeclaringType = typeof(CraftingState),
			ValueProvider = new ExtraProvider(),
			Readable = true,
			Writable = true
		});
	}
}
