using System;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.WorkerAI;

// Tasks have six priority levels, 0 to 5, instead of the game's four (locked, low, medium, high): the game's levels
// are 0 (locked), 1 (low), 3 (medium) and 5 (high), and two new ones lie half way between them, 2 and 4. The game
// weighs a task's priority by its bias number (low 0, medium 4, high 8), so the new levels are biases 2 and 6: saved
// and synced like the game's own (the same number), and a save without the mod reads them as plain numbers. The
// raise and lower buttons (and keys) step through all six. The task list shows the level's number.
[Feature(Features.PriorityLevels, Features.PriorityLevelsInfo)]
internal static class PriorityLevels
{
	// The six levels' biases, lowest first.
	private static readonly ProjectPriority.Bias[] Steps =
	{
		ProjectPriority.Bias.Locked,
		ProjectPriority.Bias.Low,
		(ProjectPriority.Bias)2,
		ProjectPriority.Bias.Medium,
		(ProjectPriority.Bias)6,
		ProjectPriority.Bias.High
	};

	// The level (0 to 5) of a bias; a bias between two levels counts as the lower one.
	internal static int Level(ProjectPriority.Bias bias)
	{
		int level = 0;
		for (int i = 0; i < Steps.Length; i++)
		{
			if ((int)bias >= (int)Steps[i])
			{
				level = i;
			}
		}
		return level;
	}

	internal static int Count => Steps.Length;

	private static void Step(Project project, int by)
	{
		int level = Math.Max(0, Math.Min(Steps.Length - 1, Level(project.PriorityBias) + by));
		if (project.PriorityBias != Steps[level])
		{
			project.PriorityBias = Steps[level];
		}
	}

	[Feature(Features.PriorityLevels, Features.PriorityLevelsInfo)]
	[HarmonyPatch(typeof(Project), nameof(Project.RaisePriority))]
	private static class RaisePatch
	{
		private static bool Prefix(Project __instance)
		{
			Step(__instance, 1);
			return false;
		}
	}

	[Feature(Features.PriorityLevels, Features.PriorityLevelsInfo)]
	[HarmonyPatch(typeof(Project), nameof(Project.LowerPriority))]
	private static class LowerPatch
	{
		private static bool Prefix(Project __instance)
		{
			Step(__instance, -1);
			return false;
		}
	}
}
