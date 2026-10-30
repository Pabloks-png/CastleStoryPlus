using System.Collections.Generic;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// A unit's AnimatorTracker follows every animation state the game waits on (about 66 on a bricktron) and asked the
// animator for its current state once per tracked state, twice for a running one, every fixed update: some 30 000
// calls into Unity a frame with a hundred bricktrons, all answering the same few layers. The animator's state does not
// change between them, so each layer's state is read once per update and every tracked state compares against it.
// The states move on exactly as before (started, finished, looped, transiting).
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(AnimatorTracker), "BrixFixedUpdate")]
internal static class AnimatorTrackerCache
{
	private const int MostLayers = 8;

	private static readonly AnimatorStateInfo[] Infos = new AnimatorStateInfo[MostLayers];

	private static readonly bool[] Read = new bool[MostLayers];

	private static readonly int[] Transition = new int[MostLayers];

	private static bool Prefix(AnimatorTracker __instance)
	{
		List<AnimatorStateProgress> states = __instance.States;
		if (!AiTickCap.Run || states == null || states.Count == 0)
		{
			return false;
		}
		Animator animator = states[0].animator;
		for (int i = 0; i < MostLayers; i++)
		{
			Read[i] = false;
			Transition[i] = -1;
		}
		for (int i = 0; i < states.Count; i++)
		{
			AnimatorStateProgress state = states[i];
			if (!ReferenceEquals(state.animator, animator) || state.layer < 0 || state.layer >= MostLayers)
			{
				state.Update();
				continue;
			}
			Step(state, animator);
		}
		return false;
	}

	// AnimatorStateProgress.Update with the layer's state read once.
	private static void Step(AnimatorStateProgress state, Animator animator)
	{
		int layer = state.layer;
		if (!Read[layer])
		{
			Infos[layer] = animator.GetCurrentAnimatorStateInfo(layer);
			Read[layer] = true;
		}
		AnimatorStateInfo info = Infos[layer];
		bool active = info.fullPathHash == state.StateHash;
		state.Started = false;
		state.Finished = false;
		state.Looped = false;
		state.Transiting = false;
		if (state.Running)
		{
			if (!active)
			{
				state.Running = false;
				state.Finished = true;
			}
			else if (InTransition(animator, layer))
			{
				state.Transiting = true;
			}
			else if (state.time > Fraction(info.normalizedTime))
			{
				state.Looped = true;
			}
		}
		else if (active)
		{
			state.Started = true;
			state.Running = true;
			state.time = Fraction(info.normalizedTime);
		}
	}

	private static bool InTransition(Animator animator, int layer)
	{
		if (Transition[layer] < 0)
		{
			Transition[layer] = animator.IsInTransition(layer) ? 1 : 0;
		}
		return Transition[layer] == 1;
	}

	private static float Fraction(float time)
	{
		return time - Mathf.Floor(time);
	}
}
