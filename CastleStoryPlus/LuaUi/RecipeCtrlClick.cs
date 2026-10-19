using BepInEx.Configuration;
using CastleStoryPlus.Core;

namespace CastleStoryPlus.LuaUi;

// Ctrl+click on a recipe in the workshop menu queues it [Crafting] CtrlClickCount times (5) instead of once. The
// recipe handles get CtrlRepeat, which the C# buttons (LuaHandleView) read on a Ctrl+click.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
internal static class RecipeCtrlClick
{
	private static ConfigEntry<int> _count;

	private static void Enable()
	{
		_count = Plugin.Cfg.Bind("Crafting", "CtrlClickCount", 5, "How many times Ctrl+click on a recipe in the workshop menu queues it (1 to 20).");
		int count = (_count.Value < 1) ? 1 : ((_count.Value > 20) ? 20 : _count.Value);
		GameUi.GameMenuItems.Extend((GameUi.GameMenuModel menu) =>
		{
			foreach (GameUi.UiItem item in menu.Popup.Find("operableRecipes").Items)
			{
				if (item is GameUi.UiButton button)
				{
					button.CtrlRepeat = () => count;
				}
			}
		});
		LuaInjection.AddPatch(Features.CsMenus, "LUI/Menus/GameMenu.lua", "h.OnAction = ||h.Visible() and Data.Operable:AddToQueue(GetRecipeAtSlotIndex(i))\n", LuaInjection.Mode.InsertAfter,
			"h.CtrlRepeat = " + count + "\n");
	}
}
