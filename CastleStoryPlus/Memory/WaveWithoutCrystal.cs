using CastleStoryPlus.Core;

namespace CastleStoryPlus.Memory;

// Invasion: a wave aims its raid at a random crystal of the players' faction. With none left (the base overrun, the
// game lost but left running) the gamemode's DoWaveSpawns made the raid, then threw on the missing crystal before the
// next wave's timer was started, so it ran again on every slow update: about one empty enemy raid (with its beacon,
// timers and overhead) a second, kept in the save. A save left like that for a while held some 900 raids, and the
// game ran at a tenth of its frame rate. Without a crystal no wave is spawned; the timer goes on as usual.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
internal static class WaveWithoutCrystal
{
	private static void Enable()
	{
		LuaInjection.AddPatch(Features.GameLeakFixes, "Gamemodes/invasion/invasion.lua", "        local crystal = crystals[ math.random(#crystals)]\n", LuaInjection.Mode.InsertAfter,
			"        ---Castle Story Plus (GameLeakFixes): no crystal to raid, no wave (it made a raid and threw, every slow update)\n"
			+ "        if crystal == nil then\n"
			+ "            return\n"
			+ "        end\n");
	}
}
