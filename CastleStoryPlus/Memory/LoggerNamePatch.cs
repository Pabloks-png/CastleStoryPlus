using Brix.Utils.Logging;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// Every static BrixLogger is named by its class's FullName. For a generic class (Signal<T>, BrixSingleton<T>, ...)
// that is a new name per closed type, with every type argument assembly-qualified (about 1.6 KB each), and log4net
// keeps every logger it has made for the rest of the game: the heap scan found 13,574 of them, 21.5 MB of names.
// The type arguments are cut off, so all closed types of one generic class share one logger. The game's log
// configuration (Info/logging.xml) sets levels by namespace (root, Brix.Network), which the shorter names keep,
// so nothing is logged differently.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(BrixLogger), MethodType.Constructor, new[] { typeof(string) })]
internal static class LoggerNamePatch
{
	private static void Prefix(ref string name)
	{
		if (name == null)
		{
			return;
		}
		int arguments = name.IndexOf('[');
		if (arguments > 0)
		{
			name = name.Substring(0, arguments);
		}
	}
}
