using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Execution.VM;

namespace CastleStoryPlus.Memory;

// Every MoonSharp Processor (one per Script, and one per Lua coroutine) makes its two stacks full size at once:
// 131,072 call frames (CallStackItem, 120 bytes each: 15 MB) and 131,072 values (DynValue, 24 bytes: 3 MB). The
// stacks never grow, so that is the deepest Lua can call. The heap scan found four of them alive (72 MB that every
// garbage collection reads), and each new Lua menu script or coroutine allocates another 18 MB at once.
// Here they start with 8,192 call frames (1 MB) and 32,768 values (768 KB): far deeper than the game's Lua goes
// (only runaway recursion gets there, and it then fails the same way, just sooner). The stacks still do not grow:
// FastStack.Push is small enough for the JIT to inline into the interpreter, so a patch on it would be skipped.
[Feature(Features.LuaStackShrink, Features.LuaStackShrinkInfo)]
[HarmonyPatch]
internal static class LuaStackShrink
{
	private const int GameSlots = 131072;

	internal const int CallFrames = 8192;

	internal const int Values = 32768;

	private static IEnumerable<MethodBase> TargetMethods()
	{
		return AccessTools.GetDeclaredConstructors(typeof(Processor)).Where(constructor => !constructor.IsStatic).Cast<MethodBase>();
	}

	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		List<CodeInstruction> code = new List<CodeInstruction>(instructions);
		int changed = 0;
		for (int i = 0; i < code.Count - 1; i++)
		{
			if (!code[i].LoadsConstant(GameSlots) || code[i + 1].opcode != OpCodes.Newobj || !(code[i + 1].operand is ConstructorInfo stack))
			{
				continue;
			}
			if (stack.DeclaringType == typeof(FastStack<CallStackItem>))
			{
				code[i].opcode = OpCodes.Ldc_I4;
				code[i].operand = CallFrames;
				changed++;
			}
			else if (stack.DeclaringType == typeof(FastStack<DynValue>))
			{
				code[i].opcode = OpCodes.Ldc_I4;
				code[i].operand = Values;
				changed++;
			}
		}
		if (changed != 2)
		{
			Plugin.Log.LogWarning("LuaStackShrink: " + changed + " of 2 Lua stacks found in " + __originalMethod + ", the others keep the game's size");
		}
		return code;
	}
}
