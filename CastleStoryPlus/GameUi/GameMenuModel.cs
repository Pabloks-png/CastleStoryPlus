using System.Collections.Generic;

namespace CastleStoryPlus.GameUi;

// The in-game menu's parts as data, for the features that add to them (GameMenuItems.Extend): the left and right
// groups, the sidebars, the bars, the task popup and a few buttons. Built and run by the reloadable UI assembly
// (CastleStoryPlus.UI: GameMenuController), which fills these in.
internal class GameMenuModel
{
	// GameMenu's exclusive left and right groups.
	public readonly UiGroup Left = new UiGroup();

	public readonly UiGroup Right = new UiGroup();

	public UiPanel NewTask;

	public UiPanel Blocks;

	public UiPanel Structures;

	public UiPanel Settings;

	// The blueprint library.
	public UiPanel Blueprints;

	// The in-game help.
	public UiPanel Help;

	// The player's debug sidebar.
	public UiPanel Debug;

	// Sidebars features add (opened from their own bar buttons).
	public readonly List<UiPanel> ExtraPanels = new List<UiPanel>();

	public readonly List<UiItem> LeftBar = new List<UiItem>();

	public readonly List<UiItem> RightBar = new List<UiItem>();

	// The selected task's popup over the task list.
	public UiPanel Popup;

	public UiButton PriorityLower;

	public UiButton PriorityRaise;

	public UiButton DeleteTask;

	public UiButton CallToArms;

	public UiButton BackToWork;

	public UiButton NewTaskButton;

	public UiButton BlocksButton;

	public UiButton StructuresButton;
}
