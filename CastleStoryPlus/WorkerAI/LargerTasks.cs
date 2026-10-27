using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Game.AI;
using Brix.Legacy;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.WorkerAI;

// Task areas can be dragged larger: every task's largest size (a harvest or base circle's radius, a quarry's or
// landscape's side) is [LargerTasks] AreaScale (1.5) times the game's, a flatten (landscape) task's [LargerTasks]
// FlattenScale (2) times. Set on every peer when the task is made, from the
// prefab's own size, so it is the same for all players and a task made again (pooled) is not scaled twice.
[Feature(Features.LargerTasks, Features.LargerTasksInfo)]
[HarmonyPatch(typeof(Project), nameof(Project.Initialize))]
internal static class LargerTasks
{
	private static ConfigEntry<float> _scale;

	private static ConfigEntry<float> _flattenScale;

	// The game's sizes, per task and per circle, before scaling.
	private static readonly Dictionary<Project, int> OwnSize = new Dictionary<Project, int>();

	private static readonly Dictionary<Beacon, int> OwnRadius = new Dictionary<Beacon, int>();

	private static void Enable()
	{
		_scale = Plugin.Cfg.Bind("LargerTasks", "AreaScale", 1.5f, new ConfigDescription("How many times larger than the game's a task's area may be dragged (radius of a circle, side of a quarry or landscape).", new AcceptableValueRange<float>(1f, 3f)));
		_flattenScale = Plugin.Cfg.Bind("LargerTasks", "FlattenScale", 2f, new ConfigDescription("How many times larger than the game's a flatten (landscape) task's area may be dragged.", new AcceptableValueRange<float>(1f, 4f)));
		GameSession.OnLeave(() =>
		{
			OwnSize.Clear();
			OwnRadius.Clear();
		});
	}

	private static void Postfix(Project __instance)
	{
		if (!OwnSize.TryGetValue(__instance, out int size))
		{
			size = __instance.MaxSize;
			OwnSize[__instance] = size;
		}
		float scale = (__instance is LandscapeProject) ? _flattenScale.Value : _scale.Value;
		__instance.MaxSize = Scaled(size, scale);
		Beacon beacon = __instance.Beacon;
		if (beacon != null)
		{
			if (!OwnRadius.TryGetValue(beacon, out int radius))
			{
				radius = beacon.MaxRadius;
				OwnRadius[beacon] = radius;
			}
			beacon.MaxRadius = Scaled(radius, scale);
		}
	}

	private static int Scaled(int size, float scale)
	{
		return Mathf.RoundToInt(size * scale);
	}
}
