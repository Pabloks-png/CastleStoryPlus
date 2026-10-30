using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Brix.Components;
using Brix.Game.Components;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Work the workers' AI did over and over for the same answer, done once.
// - Whether an object is gone (GameComponentExtender.IsNullOrReleased for a GameObject: destroyed, or released to the
//   object pool) asked the object for its first component of the game's own kind (IBrixComponent) every time, a
//   GetComponent by interface, for which Unity checks the object's components one by one against it. The AI asks it
//   all the time: every working worker's task on every tick (is the goal still there), and the task search for
//   every resource, storage and goal it weighs. That component is now remembered per object and only its released
//   and destroyed flags are read each time, as before. A remembered component that was destroyed is looked up again;
//   one added later comes after it, so the first one does not change. Asked from another thread (where Unity's
//   GetComponent is not allowed) the game's own check runs.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(GameComponentExtender), nameof(GameComponentExtender.IsNullOrReleased), new[] { typeof(GameObject) })]
internal static class ReleasedCheckCache
{
	// Objects remembered at most; the destroyed ones stay until the game is left, so the list is emptied when full.
	private const int MostObjects = 8192;

	private static readonly Dictionary<GameObject, IBrixComponent> First = new Dictionary<GameObject, IBrixComponent>(new SameObject());

	private static int _mainThread = -1;

	private static void Enable()
	{
		_mainThread = Thread.CurrentThread.ManagedThreadId;
		GameSession.OnLeave(First.Clear);
	}

	private static bool Prefix(GameObject go, ref bool __result)
	{
		if (Thread.CurrentThread.ManagedThreadId != _mainThread)
		{
			return true;
		}
		if (go == null)
		{
			__result = true;
			return false;
		}
		if (!First.TryGetValue(go, out IBrixComponent component) || (component as UnityEngine.Object) == null)
		{
			component = go.GetComponent<IBrixComponent>();
			if (component == null)
			{
				First.Remove(go);
				__result = false;
				return false;
			}
			if (First.Count >= MostObjects)
			{
				First.Clear();
			}
			First[go] = component;
		}
		__result = component.Released || component.Destroyed;
		return false;
	}

	// The same GameObject by reference: Unity's own comparison asks the engine whether the object still lives.
	private sealed class SameObject : IEqualityComparer<GameObject>
	{
		public bool Equals(GameObject a, GameObject b)
		{
			return ReferenceEquals(a, b);
		}

		public int GetHashCode(GameObject go)
		{
			return RuntimeHelpers.GetHashCode(go);
		}
	}
}
