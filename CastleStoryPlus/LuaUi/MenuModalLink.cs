using CastleStoryPlus.Core;
using MoonSharp.Interpreter;
using UnityEngine;

namespace CastleStoryPlus.LuaUi;

// [LuaUi] CsModals: the gamemodes' dialogs drawn in C# (CsModal). Every gamemode loads the same Lua dialog,
// LUI/Menus/Game/Menu_Modal.lua, into GS_Modal (the intro, victory and defeat dialogs of sandbox, conquest,
// invasion, the tutorial, grab the gems and credit island), and opens it with Hooks.Invoke(UIGame.hk_OpenModalMenu,
// {t}): t holds the texts, the header image and two buttons (btn_left, btn_right: Visible, OnAction, lbl_top/lbl_bot
// texts) as closures; the dialog's own buttons call hk_CloseModalMenu. The menu, its hooks and that table stay
// Lua; only its panels are cut out of Menu_Modal.lua, and its open and close events show and close a CsModal drawn
// from the merged table. A button calls the table's OnAction, so the game's logic (start timers, end game...) runs.
[Feature(Features.CsMenus, Features.CsMenusInfo)]
internal static class MenuModalLink
{
	private const string File = "LUI/Menus/Game/Menu_Modal.lua";

	private static void Enable()
	{
		if (!LuaUiConfig.CsModals)
		{
			return;
		}
		LuaInjection.AddFunction("CsModalOpen", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			if (args.Count > 0 && args[0].Type == DataType.Table)
			{
				Open(args[0].Table, context.OwnerScript);
			}
			return DynValue.Void;
		});
		LuaInjection.AddFunction("CsModalClose", (ScriptExecutionContext context, CallbackArguments args) =>
		{
			CsModal.Close(context.OwnerScript);
			return DynValue.Void;
		});
		// The panels: from "--panels" to the last one (lyt_botspacer).
		LuaInjection.AddRangePatch(Features.CsMenus, File, "--panels\n", "_m.lyt.lyt_botspacer = p\n_m.lyt.AddPanel(p)\nend\n", "--panels: drawn in C# (Castle Story Plus)\n");
		// After the game's own listeners: the table is merged again (whatever order the event calls them in) and shown.
		LuaInjection.AddPatch(Features.CsMenus, File, "_t.ev_close:AddListener(function() return _t:_SetCfg() end)\n", LuaInjection.Mode.InsertAfter,
			"_t.ev_open:AddListener(function(t) _t:_SetCfg(t, true) CastleStoryPlus.CsModalOpen(_t.t_cfg) end)\n" +
			"_t.ev_close:AddListener(function() CastleStoryPlus.CsModalClose() end)\n");
		GameSession.OnLeave(CsModal.Close);
	}

	private static void Open(Table cfg, Script script)
	{
		ModalSpec spec = new ModalSpec
		{
			Header = Texture(LuaBridge.Value(LuaBridge.GetTable(cfg, "rawimg_header"), "Tex")),
			Title = Text(cfg, "lbl_title"),
			Subtitle = Text(cfg, "lbl_subtitle"),
			Body = Text(cfg, "lbl_content")
		};
		// Enter: the only button, or with both the left one (start, keep playing as a spectator); the right one
		// ends the game in the victory and defeat dialogs.
		AddButton(spec, LuaBridge.GetTable(cfg, "btn_left"));
		AddButton(spec, LuaBridge.GetTable(cfg, "btn_right"));
		spec.DefaultButton = (spec.Buttons.Count > 0) ? 0 : -1;
		CsModal.Show(spec, script);
		if (script != null)
		{
			script.OnScriptKilled -= OnScriptKilled;
			script.OnScriptKilled += OnScriptKilled;
		}
		Plugin.Log.LogInfo("CsMenus: dialog \"" + spec.Title + "\" drawn in C#");
	}

	private static void OnScriptKilled(Script script)
	{
		script.OnScriptKilled -= OnScriptKilled;
		CsModal.Close(script);
	}

	private static void AddButton(ModalSpec spec, Table button)
	{
		if (button == null || !LuaBridge.Bool(LuaBridge.Value(button, "Visible"), true))
		{
			return;
		}
		spec.Buttons.Add(new ModalButton
		{
			Title = Text(button, "lbl_top"),
			Info = Text(button, "lbl_bot"),
			// Read when clicked, as the Lua button did; it closes the dialog itself (hk_CloseModalMenu).
			OnClick = () => LuaBridge.Value(button, "OnAction"),
			KeepOpen = true
		});
	}

	// t.<label>.Text: a closure answering the (already localised) text.
	private static string Text(Table owner, string label)
	{
		Table table = LuaBridge.GetTable(owner, label);
		return (table != null) ? LuaBridge.String(LuaBridge.Value(table, "Text"), null) : null;
	}

	private static Texture Texture(DynValue value)
	{
		if (value.Type != DataType.UserData || value.UserData == null)
		{
			return null;
		}
		if (value.UserData.TryGet<Texture2D>(out Texture2D texture2D))
		{
			return texture2D;
		}
		if (value.UserData.TryGet<Texture>(out Texture texture))
		{
			return texture;
		}
		return null;
	}
}
