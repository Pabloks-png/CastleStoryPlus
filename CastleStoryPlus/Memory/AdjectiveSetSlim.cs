using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Brix.Game.AI;
using CastleStoryPlus.Core;
using HarmonyLib;

namespace CastleStoryPlus.Memory;

// Every DicoAdjectif (the adjective set of a description: what a thing is, holds or may hold) made two dictionaries
// (its field's initializer and then its constructor's), a StringWriter with its StringBuilder and buffer, used only
// when Unity serializes the set. Descriptions make new sets in all their sums and differences (stockpile capacities,
// what a worker carries, what a recipe needs), many times a second, and the heap scan found some 70 000 alive. A set
// now makes only its constructor's dictionary, and its StringWriter the first time it is serialized.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch]
internal static class AdjectiveSetSlim
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Constructor(typeof(DicoAdjectif), Type.EmptyTypes);
		yield return AccessTools.Constructor(typeof(DicoAdjectif), new[] { typeof(int) });
	}

	// The field initializers come first in each constructor: the first new Dictionary() and the new StringWriter()
	// become null (the constructor's own dictionary follows).
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		ConstructorInfo dictionary = AccessTools.Constructor(typeof(Dictionary<Type, Adjectif>), Type.EmptyTypes);
		ConstructorInfo writer = AccessTools.Constructor(typeof(StringWriter), Type.EmptyTypes);
		bool dictionaryDone = false;
		bool writerDone = false;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Newobj && !dictionaryDone && Equals(instruction.operand, dictionary))
			{
				dictionaryDone = true;
				yield return new CodeInstruction(OpCodes.Ldnull).MoveLabelsFrom(instruction);
				continue;
			}
			if (instruction.opcode == OpCodes.Newobj && !writerDone && Equals(instruction.operand, writer))
			{
				writerDone = true;
				yield return new CodeInstruction(OpCodes.Ldnull).MoveLabelsFrom(instruction);
				continue;
			}
			yield return instruction;
		}
		if (!dictionaryDone || !writerDone)
		{
			Plugin.Log.LogWarning("AdjectiveSetSlim: the field initializers were not found in " + original + "; left as it is");
		}
	}
}

// AdjectiveSetSlim's StringWriter, made when the set is first serialized.
[Feature(Features.GameLeakFixes, Features.GameLeakFixesInfo)]
[HarmonyPatch(typeof(DicoAdjectif), nameof(DicoAdjectif.OnBeforeSerialize))]
internal static class AdjectiveSetWriter
{
	private static void Prefix(DicoAdjectif __instance)
	{
		if (__instance.stringWriter == null)
		{
			__instance.stringWriter = new StringWriter();
		}
	}
}
