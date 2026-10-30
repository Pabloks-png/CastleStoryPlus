using System;
using System.Collections;
using CastleStoryPlus.Core;
using UnityEngine;

namespace CastleStoryPlus.Menus;

// Development: with CSP_AUTOLOAD set the game loads a save by itself once the title screen is up, as CONTINUE does:
// "latest" (or "1") the newest save, anything else the newest save whose folder name holds it. Only the first title
// screen after the start (or after entering play mode in the editor) does it, so leaving to the menu stays there.
internal class AutoContinue : MonoBehaviour
{
	private const string Variable = "CSP_AUTOLOAD";

	// Seconds the title screen gets to settle (its menus open over a few frames) before the save is loaded.
	private const float SettleSeconds = 1.5f;

	private static bool _done;

	internal static void Start()
	{
		string wanted = Environment.GetEnvironmentVariable(Variable);
		if (_done || string.IsNullOrEmpty(wanted))
		{
			return;
		}
		_done = true;
		Plugin.Root.AddComponent<AutoContinue>().StartCoroutine(Load(wanted));
	}

	private static IEnumerator Load(string wanted)
	{
		yield return new WaitForSecondsRealtime(SettleSeconds);
		string name = wanted == "1" || wanted.Equals("latest", StringComparison.OrdinalIgnoreCase) ? null : wanted;
		Plugin.Log.LogInfo("Auto-continue (" + Variable + "=" + wanted + ")");
		ContinueClickPatch.ContinueSave(name);
	}
}
