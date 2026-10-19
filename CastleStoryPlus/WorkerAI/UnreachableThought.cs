using System.Collections.Generic;
using Brix.Game.AI;
using Brix.Game.Semantique;
using Brix.Lua;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// "Can't reach my destination" tells what: when a worker's path fails, the failure's destination (the thing it
// walked to, else the spot) is kept for that worker, and its thought bubble shows it after the game's text. The
// thought itself is one shared, synced value, so only the host (where the workers' AI runs) knows the destination.
[Feature(Features.UnreachableThought, Features.UnreachableThoughtInfo)]
internal static class UnreachableThought
{
	private const float KeepSeconds = 10f;

	private sealed class Failure
	{
		public string Text;

		public float At;
	}

	private static readonly Dictionary<Labor, Failure> Failures = new Dictionary<Labor, Failure>();

	private static void Enable()
	{
		GameSession.OnLeave(Failures.Clear);
	}

	internal static void Record(Labor labor, Exceptions.PathException exception)
	{
		Location location = exception.Location;
		if (labor == null || location == null || location.IsEmpty)
		{
			return;
		}
		string text = Describe(labor, location);
		if (text == null)
		{
			return;
		}
		if (Failures.Count > 512)
		{
			Failures.Clear();
		}
		Failures[labor] = new Failure { Text = text, At = Time.time };
	}

	// What the worker could not reach, for its bubble; null when it is not known (or too old).
	internal static string For(Labor labor)
	{
		if (labor == null || !Failures.TryGetValue(labor, out Failure failure) || Time.time - failure.At > KeepSeconds)
		{
			return null;
		}
		return failure.Text;
	}

	private static string Describe(Labor labor, Location location)
	{
		GameObject target = location.Object;
		string what = (target != null) ? NameOf(target) : null;
		Vector3 to = location.HasSpecificPosition ? location.Position : ((target != null) ? target.transform.position : labor.transform.position);
		int distance = Mathf.RoundToInt(Vector3.Distance(labor.transform.position, to));
		int up = Mathf.RoundToInt(to.y - labor.transform.position.y);
		string height = (up >= 2) ? ", " + up + " up" : ((up <= -2) ? ", " + (-up) + " down" : string.Empty);
		return (what ?? "a spot") + ", " + distance + " blocks away" + height;
	}

	// The thing's resource name (a log, a stone block), else its object name.
	private static string NameOf(GameObject go)
	{
		try
		{
			Ressource resource = Apparence.PrincipaleOf(go);
			if (resource != null)
			{
				LuaCrafting.LuaResource lua = LuaCrafting.ToLuaResource(resource);
				if (lua != LuaCrafting.LuaResource.None)
				{
					return GameUi.CraftingCatalog.ResourceName(lua);
				}
			}
		}
		catch (System.Exception)
		{
		}
		string name = go.name.Replace("(Clone)", string.Empty).Trim();
		return string.IsNullOrEmpty(name) ? null : name;
	}
}

[Feature(Features.UnreachableThought, Features.UnreachableThoughtInfo)]
[HarmonyPatch(typeof(BaseLabor), nameof(BaseLabor.HandleWorkException))]
internal static class UnreachableThoughtRecordPatch
{
	private static void Prefix(BaseLabor __instance, Exceptions.WorkFailedException exception)
	{
		if (exception is Exceptions.PathException path && __instance is Labor labor)
		{
			UnreachableThought.Record(labor, path);
		}
	}
}

// The bubble: the game's "Can't reach my destination", then what.
[Feature(Features.UnreachableThought, Features.UnreachableThoughtInfo)]
[HarmonyPatch(typeof(BricktronOverheadDisplayDriver), "UpdateThought")]
internal static class UnreachableThoughtBubblePatch
{
	private static void Postfix(BricktronOverheadDisplayDriver __instance)
	{
		if (__instance.state == null || __instance.state.Thought != Exceptions.PathException.CantReachDestination)
		{
			return;
		}
		string what = UnreachableThought.For(__instance.labor);
		if (what == null)
		{
			return;
		}
		__instance.overheadDisplay.ThoughtBubble.Get.SetThought(GameUi.GameUiState.Text(Exceptions.PathException.CantReachDestination.GetLabel()) + ": " + what, important: true);
	}
}
