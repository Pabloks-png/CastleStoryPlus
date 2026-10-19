using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// Every DicoAdjectif (the adjective set of a game object) keeps its adjectives as JSON text for Unity's serializer,
// and every clone Unity makes gets its own copy of that text, about 2 KB each: the heap scan found 22 MB of these
// strings, almost all the same few texts. The game already keeps one set per text (deserializeCache, by md5), so a
// set that matches the cached one now shares the cached set's strings instead of keeping its own copy.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(DicoAdjectif), nameof(DicoAdjectif.OnAfterDeserialize))]
internal static class AdjectiveTextSharing
{
	private static void Postfix(DicoAdjectif __instance)
	{
		string md5 = __instance.md5;
		if (md5 == null || DicoAdjectif.deserializeCache == null || !DicoAdjectif.deserializeCache.TryGetValue(md5, out DicoAdjectif cached))
		{
			return;
		}
		if (cached == null || ReferenceEquals(cached, __instance) || ReferenceEquals(cached.adjectives, __instance.adjectives))
		{
			return;
		}
		if (cached.adjectives == __instance.adjectives)
		{
			__instance.adjectives = cached.adjectives;
			__instance.md5 = cached.md5;
		}
	}
}
