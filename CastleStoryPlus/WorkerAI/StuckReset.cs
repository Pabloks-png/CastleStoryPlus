using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Game;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.WorkerAI;

// Failsafe for a worker that hangs in a task: one that has not moved for [StuckReset] Seconds (10) in a task that is
// a walk (going to, picking up from, running or walking towards something), or for LongSeconds (60) in any other
// task, has the task cancelled and puts down what it carries where it stands, then looks for work (or follows a
// waiting order) afresh. Workshop workers (who stand at their station) and fighters are left alone. Each reset is
// written to the log. Host only.
[Feature(Features.StuckReset, Features.StuckResetInfo)]
internal sealed class StuckReset : MonoBehaviour
{
	private const float CheckSeconds = 1f;

	// Moving less than this (blocks) counts as standing still.
	private const float MoveTolerance = 0.5f;

	private sealed class Watch
	{
		public Vector3 Anchor;

		public float Since;

		public object Task;
	}

	private static ConfigEntry<float> _seconds;

	private static ConfigEntry<float> _longSeconds;

	private static readonly Dictionary<Labor, Watch> Watches = new Dictionary<Labor, Watch>();

	private readonly List<Labor> _labors = new List<Labor>();

	private float _next;

	private static void Enable()
	{
		_seconds = Plugin.Cfg.Bind("StuckReset", "Seconds", 10f, new ConfigDescription("A worker that has not moved this long in a walking task has the task cancelled and puts down what it carries (0: off).", new AcceptableValueRange<float>(0f, 120f)));
		_longSeconds = Plugin.Cfg.Bind("StuckReset", "LongSeconds", 60f, new ConfigDescription("The same for any other task (building, digging...), which may stand still longer (0: off).", new AcceptableValueRange<float>(0f, 600f)));
		GameSession.OnLeave(Watches.Clear);
		Plugin.Root.AddComponent<StuckReset>();
	}

	private void Update()
	{
		if (!NetworkServer.active || Time.time < _next)
		{
			return;
		}
		_next = Time.time + CheckSeconds;
		Live<Labor>.Active(_labors);
		foreach (Labor labor in _labors)
		{
			try
			{
				Check(labor);
			}
			catch (System.Exception ex)
			{
				Plugin.Log.LogWarning("StuckReset: " + ex.Message);
				Watches.Remove(labor);
			}
		}
	}

	private static void Check(Labor labor)
	{
		Task task = (labor != null) ? labor.CurrentTask : null;
		if (task == null || labor.IsPhasedOut || labor.Operable != null || labor.Activity == Activity.Fighting || !Watched(labor))
		{
			Watches.Remove(labor);
			return;
		}
		Vector3 at = labor.transform.position;
		if (!Watches.TryGetValue(labor, out Watch watch) || watch.Task != task || (at - watch.Anchor).sqrMagnitude > MoveTolerance * MoveTolerance)
		{
			Watches[labor] = new Watch { Anchor = at, Since = Time.time, Task = task };
			return;
		}
		float limit = IsWalk(task) ? _seconds.Value : _longSeconds.Value;
		if (limit <= 0f || Time.time - watch.Since < limit)
		{
			return;
		}
		Plugin.Log.LogInfo("StuckReset: " + labor.name + " stood still " + Mathf.RoundToInt(Time.time - watch.Since) + " s at " + Mathf.FloorToInt(at.x) + "," + Mathf.FloorToInt(at.y) + "," + Mathf.FloorToInt(at.z) + " in '" + task.Description + "'" + (labor.recepteur.IsEmpty() ? string.Empty : (" carrying " + labor.recepteur.ContentString())) + ": task cancelled, load put down");
		Watches.Remove(labor);
		labor.AbortWorkSignal.Invoke(TaskResult.ProjectCancelledTask);
		if (!labor.recepteur.IsEmpty())
		{
			labor.recepteur.DropAll();
		}
	}

	// The player's workers only.
	private static bool Watched(Labor labor)
	{
		Faction faction = Faction.GetFaction(labor.gameObject);
		return faction != null && !faction.isAI;
	}

	private static bool IsWalk(Task task)
	{
		string name = task.Description ?? string.Empty;
		return name.StartsWith("Go") || name.StartsWith("Run") || name.StartsWith("Walk");
	}
}
