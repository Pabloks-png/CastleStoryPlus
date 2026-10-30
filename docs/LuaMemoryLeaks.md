# Lua memory leaks

Castle Story draws its menus with Lua (MoonSharp, the LUI menus in `Info/Lua/LUI`). In October 2026 the heap scan
(`POST /api/heap/scan`, see [DevTools](DevTools.md)) found Lua behind about 110 MB of a 485 MB live heap and most of
the allocations per frame, and a map load spent about 25 s building GameMenu and 11 s the gamemode panel. The Lua
interface leaked in two ways: memory held for good by how MoonSharp is set up, and menus the game never let go of.
This page tells what was found and how each was fixed. The fixes are features `LuaHeapShrink`, `LuaStackShrink`,
`GameLeakFixes` and `CsMenus` (see [Features](Features.md)); the code is in `CastleStoryPlus/Memory` and
`CastleStoryPlus/GameUi`.

## How it was found

1. The performance window (F7) showed the managed heap growing on every trip into a game and back, and long garbage
   collection pauses (about 450 ms) even when little was allocated.
2. The heap scan listed the types holding the most memory, the static field or component that reached them (root)
   and the field they sat in (holder): `GET /api/heap?holderType=...`.
3. Each finding was checked in the game's code (the decompiled source) to see who adds to it and who should remove it,
   then fixed with a Harmony patch and checked with a new scan after a few trips to the menu and back.

## Memory MoonSharp holds for good

**The captured value heap (`LuaHeapShrink`).** MoonSharp keeps every value a Lua closure captures in one static array
of 10,485,760 slots (`HeapAllocatedDynValue`), made full size when Lua first runs: 240 MB of references plus 40 MB of
counts. The game uses a small part of it, but every garbage collection reads the whole array for references, a quarter
of what a collection walked. The arrays now start at 1,048,576 slots and double when the slots run out, as a `List`
does. The check for room is two field reads, so the common call costs nothing extra.

**The interpreter stacks (`LuaStackShrink`).** Every MoonSharp processor (one per script and one per Lua coroutine)
made its call stack and value stack full size at once: 131,072 call frames (15 MB) and 131,072 values (3 MB). The
heap scan found four alive (72 MB that every collection reads), and every Lua menu script or coroutine allocated
another 18 MB in one go. They now hold 8,192 call frames and 32,768 values (under 2 MB). The game's Lua never goes
near that depth; runaway recursion fails the same way, only sooner. The stacks still do not grow: `FastStack.Push` is
inlined by the JIT into the interpreter, so a patch on it would be skipped.

## Menus the game never let go of

**Lua menus destroyed with their scene (`LuaScriptLeakPatch`).** A Lua menu component that loads its menu file owns
the script it runs in. When the component was destroyed with its scene (title screen, lobby, options and others, on
every trip into a game and back), `Release` only forgot the script; it was never killed. The script stayed alive with
all its tables, closures and C# panels (the static widget maps only drop entries of dead scripts), and its hooks on the
session's update and slow update signals kept running. Only menus unloaded from Lua were closed properly. The script
is now closed the way `Unload` does it: hooks dropped, widget maps cleared, script killed.

**Dead closures on the game's hooks (`LuaHookKillPatch`).** Closing those scripts uncovered the next problem. A Lua
closure connected to one of the game's C# hooks (project selected, crew info and others) is only disconnected when its
script dies if the script has `hk_OnScriptKilled` in its globals, and the menu components' scripts have none. Their
closures died while still connected, and in the next game the first dead one threw "Attempting to Call on dead
Closure", which ended the whole hook call: GameMenu's own listener after it never ran, so the Structures and Blocks
buttons did nothing. A closure whose script has no `hk_OnScriptKilled` is now disconnected when its script is killed.

**The Lua id map (`LuaMenuIdPatch` in `GameLeakPatches`).** `LuaIdUtility.UnregisterMenu` removed the entry of the
newest id instead of the menu's own. Every closed Lua menu (and its panels, which are never unregistered) stayed in
the id map for the session, and the newest live entry could be removed by mistake. It now removes the menu's own id and its panels' ids.

**Observers that outlive their game (`SignalLeakPatches`).** The game observer's world editor objects, drag handles,
dynamic object lists and each game's overhead parenter connected to signals that live for the whole session and threw
the connection away, so every game played left them (and all they tracked) behind, still called on every later
event. The connections are kept and closed when their owner goes.

## Replacing the Lua interface (`CsMenus`)

The fixes above stop the leaks, but the Lua interface stayed expensive: building GameMenu (about 270 Lua menus, some
80,000 widget inits) took about 25 s of every map load and its scripts allocated most of the garbage per frame. The
menus were therefore rebuilt in C# (uGUI), one part at a time, each behind a `[LuaUi]` switch with the Lua menu as
fallback:
1. First the views only: the Lua menu handles and groups stayed (labels, actions, hotkeys) and C# drew them
   (scoreboards, bars, build catalogues, tooltips, task popup, task list, blueprint library). This paid for both.
2. Then GameMenu as a whole (`[LuaUi] CsGameMenu`, on by default since 18-10-2026): `LUI/Menus/GameMenu.lua` is no
   longer loaded, and the C# menu reads the game's own C# objects instead of the Lua data modules. The gamemode panel
   (`CsScoreboard`) and dialogs (`Menu_Modal.lua`) are C# too. Only the gamemodes' rules (invasion waves and the like)
   are still Lua.

With GameMenu gone, its scripts, closures, widget maps and per-frame Lua allocations are gone, and the stack and heap
fixes above keep what Lua remains small. Big Lua files that are still loaded are compiled once and kept
(`FasterLoading`, `BepInEx/cache/CastleStoryPlus/LuaCompile`).

## Checking for new leaks

- Note `memory` in `GET /api/frames` (used after the last collection), go to the title screen and back into the same
  save a few times, and compare: it should come back to about the same size.
- Run a heap scan before and after and compare the types and holders that grew.
- A Lua menu that leaks shows up as `MoonSharp` types (`Closure`, `Table`, `DynValue[]`) held by `LuaIdUtility`,
  the widget maps or a signal's connection list.
