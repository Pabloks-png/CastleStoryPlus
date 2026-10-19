using System.Collections.Generic;
using CastleStoryPlus.Core;
using MoonSharp.Interpreter;

namespace CastleStoryPlus.LuaUi;

// [LuaUi] CsBuildMenus: the structure catalogue's buttons tell what a building is for. The game's buttons only show
// the building's name; GameMenu's six structure sections (storage, production, crafting, military, wards,
// decoration) give each button a Tooltip of its name and a line on what it does, keyed by the blueprint's name. The
// C# sidebar shows a handle's Tooltip in place of its label; a building without a text keeps its name alone.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
internal static class BuildMenuTooltips
{
	private const string Section = "local meta = Meta.Structure\nfor i = 1, #meta do\nlocal k = meta[i]\nif meta[k].groupId == {0} then\nlocal h = ButtonHandle.New()\n";

	private static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
	{
		{ "Pallet", "Stores resources. The workers bring what they gather here and fetch from it for building and crafting." },
		{ "Toolrack", "Stores tools, weapons and unit kits for the bricktrons to take." },
		{ "ChoppingBlock", "Saws logs into planks, the main resource for wooden structures." },
		{ "Loom", "Spins plant fibre into fabric and rope." },
		{ "Workbench", "Crafts halberdier kits, archer kits and lantern wards." },
		{ "Lab", "Crafts anything explosive: bombs, explosive barrels and alchemist kits." },
		{ "Altar", "Crafts items drawing on the crystals' energy: artificer kits and the sentinel, healing, capture and pylon wards." },
		{ "MachineShop", "Crafts mechanical contraptions: arbalist kits and bear traps." },
		{ "RopeBridgePlacement", "A rope bridge between two points, so bricktrons can walk across gaps." },
		{ "ShakyDoor", "A cheap, weak door. Your bricktrons walk through, enemies have to break it." },
		{ "Door", "A door in a wall. Your bricktrons walk through, enemies have to break it." },
		{ "HeavyDoor", "A strong door that holds out longest against enemies. Your bricktrons walk through." },
		{ "Catapult", "Siege weapon worked by a bricktron. Throws explosive barrels (crafted at the laboratory) at a target." },
		{ "Barrel", "An explosive barrel: ammunition for the catapult." },
		{ "BearTrap", "Holds any enemy that steps on it in place for a while." },
		{ "PylonGround", "Relays power from a crystal to other wards." },
		{ "LanternWard", "Lights up its surroundings; brighter when connected to a power source." },
		{ "SentinelWard", "Attacks enemies near it." },
		{ "HealingWard", "Heals your bricktrons in its radius over time." },
		{ "CaptureWard", "Placed beside a crystal shard, it captures the shard." },
		{ "Window", "Decoration: a window to set into a wall." },
		{ "DoubleWindow", "Decoration: a double window to set into a wall." },
		{ "KnightStatue", "Decoration: a knight statue." },
		{ "PaintingSmall", "Decoration: a small painting." },
		{ "PaintingBig", "Decoration: a big painting." },
		{ "PaintingWide", "Decoration: a wide painting." },
		{ "PaintingTall", "Decoration: a tall painting." },
		{ "LargePalette", "A 3 x 3 stockpile holding three times as much as the normal one." },
		{ "Warehouse", "A big storage hall holding 4500 resources of any kind; workers store and fetch inside. Can turn stored stone into bricks (a switch in its menu)." },
		{ "Smithy", "Researches weapon upgrades for the whole team: melee, block and arrow damage, archer aim and crossbow reload, in three tiers." },
		{ "Armoury", "Researches armour upgrades for the soldiers: less melee, arrow and magic damage taken, more health and better shields, in three tiers." },
		{ "ResearchStation", "Researches colony upgrades paid in dark crystals: more bricktrons, work and walking speed, storage room, cheaper bricktrons and more." },
		{ "Market", "Trades any resource for another. Prices rise with trading and recover over time." }
	};

	private static void Enable()
	{
		if (!LuaUiConfig.CsBuildMenus)
		{
			return;
		}
		// The C# catalogue: every structure, the mod's too (added by their features' Enable, which may run later).
		GameUi.GameMenuItems.Extend((GameUi.GameMenuModel menu) =>
		{
			foreach (GameUi.BuildCatalog.Entry entry in GameUi.BuildCatalog.Structures)
			{
				GameUi.BuildCatalog.Entry e = entry;
				if (e.Tooltip == null && Text(e.Key.Name) != null)
				{
					e.Tooltip = () => For(e.Key.Name, e.Name());
				}
			}
		});
		LuaInjection.AddFunction("BuildingTooltip", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			string key = (args.Count > 0 && args[0].Type == DataType.String) ? args[0].String : null;
			string text = Text(key);
			if (text == null)
			{
				return DynValue.Nil;
			}
			string name = (args.Count > 1 && args[1].Type == DataType.String) ? args[1].String : key;
			return DynValue.NewString("<color=#ffc50a><b>" + name + "</b></color>\n" + text);
		});
		for (int group = 1; group <= 6; group++)
		{
			LuaInjection.AddPatch(Features.CsMenus, "LUI/Menus/GameMenu.lua", Section.Replace("{0}", group.ToString()), LuaInjection.Mode.InsertAfter,
				"h.Tooltip = ||CastleStoryPlus.BuildingTooltip(k.name, meta[k].Name())\n");
		}
	}

	// The tooltip: the building's name, then what it is for; null when there is no text for it.
	internal static string For(string key, string name)
	{
		string text = Text(key);
		return (text == null) ? null : "<color=#ffc50a><b>" + (name ?? key) + "</b></color>\n" + text;
	}

	// What the building is for; the furnace and the forge also tell the mod's coal and steel recipes.
	private static string Text(string key)
	{
		if (key == null)
		{
			return null;
		}
		bool metallurgy = Plugin.IsEnabled(Features.Metallurgy);
		if (key == "Furnace")
		{
			return metallurgy ? "Smelts raw iron into iron ingots and makes glass, and burns logs into coal." : "Smelts raw iron into iron ingots and makes glass.";
		}
		if (key == "Forge")
		{
			return metallurgy ? "Metalworking: turns iron ingots into knight kits and cogs, and iron with coal into steel." : "Metalworking: turns iron ingots into knight kits and cogs.";
		}
		return Texts.TryGetValue(key, out string text) ? text : null;
	}
}
