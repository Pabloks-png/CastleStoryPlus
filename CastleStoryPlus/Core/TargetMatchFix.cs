#if UNITY6
using Brix.Game.AI;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Core;

// Unity 6: bricktrons could not climb up or step down a block; they played the climb on the spot over and over and
// could not leave a quarry. Both moves reach their goal by target matching (Animator.MatchTarget, TargetMatcher), and
// the unit moves by its animator's motion, which Locomotion4 reads and applies itself (applyRootMotion off, no
// OnAnimatorMove). Unity 5.6 matched the target that way too; Unity 6 starts the match (isMatchingTarget) but never
// bends the motion towards the target, so the climb ended where it began (TargetMatcher.MatchFailed). With an
// OnAnimatorMove on the unit the root motion counts as handled by a script: the target matching runs again and the
// handler leaves the transform alone, so Locomotion4 still applies the motion as before.
[Feature]
[HarmonyPatch(typeof(Locomotion4), "InitAnimator")]
internal static class TargetMatchFix
{
	private static void Enable()
	{
		// Units that are already in the world (a mod reload).
		foreach (Locomotion4 locomotion in Object.FindObjectsOfType<Locomotion4>())
		{
			Add(locomotion);
		}
	}

	private static void Postfix(Locomotion4 __instance)
	{
		Add(__instance);
	}

	private static void Add(Locomotion4 locomotion)
	{
		if (locomotion != null && locomotion.GetComponent<Animator>() != null && locomotion.GetComponent<ScriptRootMotion>() == null)
		{
			locomotion.gameObject.AddComponent<ScriptRootMotion>();
		}
	}
}

// Takes the animator's root motion as handled by script and does nothing with it (Locomotion4 applies it).
internal sealed class ScriptRootMotion : MonoBehaviour
{
	private void OnAnimatorMove()
	{
	}
}
#endif
