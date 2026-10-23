using Brix.Common.Collections;
using Brix.Game.AI;
using Brix.Game.Semantique;
using Brix.Lua;
using Brix.Utils;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;

namespace CastleStoryPlus.LuaUi;

// A storage's content for Lua (the stockpile lists, the scoreboard): the game looks every resource up in the Lua
// resource table and throws for one it has none for (raw glass, blue crystal chunks, purified brimstone held in a
// stockpile), which stopped the scoreboard and with it the minimap from being made. Those resources are left out.
[Feature]
[HarmonyPatch(typeof(LuaExtenders), nameof(LuaExtenders.ToDynTable))]
internal static class StorageListFix
{
	private static bool Prefix(Description desc, Script script, int emptyCount, ref DynValue __result)
	{
		DynValue result = DynValue.NewTable(script);
		foreach (Ressource item in desc.GetRessources().NoLeak())
		{
			if (item.valeur == 0 || !LuaCrafting._adjectiveToLuaResource.TryGetValue(item.GetKey(), out LuaCrafting.LuaResource resource))
			{
				continue;
			}
			result.Table.Set(DynValue.FromObject(script, resource), DynValue.NewNumber(item.valeur));
		}
		result.Table.Set(LuaCrafting.LuaResource.None, DynValue.NewNumber(emptyCount));
		__result = result;
		return false;
	}
}
