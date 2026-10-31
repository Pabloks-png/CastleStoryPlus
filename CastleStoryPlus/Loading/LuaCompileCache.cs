using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;
using CastleStoryPlus.Core;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Execution.VM;

namespace CastleStoryPlus.Loading;

// Big Lua files (GameMenu.lua is 150 KB with the mod's patches) are parsed and compiled again into every new
// script, once per map load; parsing is most of their load time and its garbage triggers collections. The first
// compile of a file is kept (in memory and in BepInEx/cache) and later loads of the same text copy the compiled
// instructions into the new script instead.
// MoonSharp's own binary dump is not used: it drops every line number (errors and the profilers would show
// line 0) and does not move the start address of nested functions, so they run the wrong code in another script.
// This copy keeps every instruction field and source position, moves all code addresses (jumps and function
// starts) and registers the source text under the same name, as LoadString does. Anything it does not know how
// to copy (other value types, addresses outside the file) is not cached and compiles as before.
[Feature(Features.FasterLoading, Features.FasterLoadingInfo)]
[HarmonyPatch(typeof(Script), nameof(Script.LoadString))]
internal static class LuaCompileCache
{
	private const int FormatVersion = 1;

	private const uint Magic = 0x43504C43;

	// Smaller files compile in a few milliseconds; not worth a cache file each.
	private const int MinLength = 8192;

	private const byte NoValue = 255;

	private struct Op
	{
		public byte Code;

		public bool Address;

		public int NumVal;

		public int NumVal2;

		public string Name;

		public byte ValueType;

		public double Number;

		public string String;

		public int Symbol;

		public int[] SymbolList;

		public int Ref;
	}

	private struct Sym
	{
		public bool DefaultEnv;

		public byte Type;

		public int Index;

		public string Name;

		public int Env;
	}

	// One source position; positions with the same Group shared one SourceRef Id when compiled. Group -1 = none.
	private struct Ref
	{
		public int Group;

		public int FromChar;

		public int ToChar;

		public int FromLine;

		public int ToLine;

		public bool StepStop;

		public bool CannotBreakpoint;

		public bool Breakpoint;
	}

	private class Compiled
	{
		public string Name;

		public string Code;

		public int Groups;

		public Op[] Ops;

		public Sym[] Symbols;

		public Ref[] Refs;

		// SourceCode.Refs (the debugger's breakpoint positions), as indices into Refs.
		public int[] SourceRefs;
	}

	internal static ConfigEntry<bool> Enabled;

	private static readonly Dictionary<string, Compiled> Memory = new Dictionary<string, Compiled>();

	// Texts that cannot be cached, so they are not checked again on every load.
	private static readonly Dictionary<string, string> Rejected = new Dictionary<string, string>();

	private static string _compiler;

	public static int Restored;

	public static int Stored;

	private static void Enable()
	{
		Enabled = Plugin.Cfg.Bind("Loading", "LuaCompileCache", true, "Lua files of 8 KB and more are compiled once and kept in BepInEx/cache/CastleStoryPlus/LuaCompile; later loads of the same text (with the mod's patches) copy the compiled code, line numbers included, instead of parsing the file again.");
	}

	private static string CacheDir => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "CastleStoryPlus"), "LuaCompile");

	// Compiled code is only valid for the MoonSharp build that made it.
	private static string Compiler => _compiler ?? (_compiler = typeof(Script).Module.ModuleVersionId.ToString());

	private static bool Prefix(Script __instance, string code, Table globalTable, string codeFriendlyName, ref DynValue __result, out int __state)
	{
		__state = -1;
		if ((Enabled != null && !Enabled.Value) || codeFriendlyName == null || code == null || code.Length < MinLength || !__instance.IsAlive || code.StartsWith("MoonSharp_dump_b64::", StringComparison.Ordinal))
		{
			return true;
		}
		lock (Memory)
		{
			if (Rejected.TryGetValue(codeFriendlyName, out string rejected) && rejected == code)
			{
				return true;
			}
		}
		Compiled compiled = Find(codeFriendlyName, code);
		if (compiled != null)
		{
			try
			{
				__result = Restore(__instance, compiled, globalTable);
				Restored++;
				return false;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("Lua compile cache: " + codeFriendlyName + " could not be restored, compiling it: " + ex);
				Forget(codeFriendlyName);
			}
		}
		__state = __instance.m_ByteCode.Code.Count;
		return true;
	}

	// Runs only when the compile succeeded.
	private static void Postfix(Script __instance, string code, string codeFriendlyName, int __state)
	{
		if (__state < 0)
		{
			return;
		}
		try
		{
			Compiled compiled = Capture(__instance, __state, codeFriendlyName, code);
			if (compiled == null)
			{
				lock (Memory)
				{
					Rejected[codeFriendlyName] = code;
				}
				return;
			}
			lock (Memory)
			{
				Memory[codeFriendlyName] = compiled;
			}
			Write(compiled);
			Stored++;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("Lua compile cache: " + codeFriendlyName + " not cached: " + ex);
		}
	}

	private static Compiled Find(string name, string code)
	{
		Compiled compiled;
		lock (Memory)
		{
			Memory.TryGetValue(name, out compiled);
		}
		if (compiled != null && compiled.Code == code)
		{
			return compiled;
		}
		compiled = Read(name, code);
		if (compiled != null)
		{
			lock (Memory)
			{
				Memory[name] = compiled;
			}
		}
		return compiled;
	}

	private static void Forget(string name)
	{
		lock (Memory)
		{
			Memory.Remove(name);
		}
		try
		{
			File.Delete(PathFor(name));
		}
		catch (Exception)
		{
		}
	}

	private static bool IsAddress(OpCode op)
	{
		switch (op)
		{
			case OpCode.Jump:
			case OpCode.Jf:
			case OpCode.JNil:
			case OpCode.JFor:
			case OpCode.JtOrPop:
			case OpCode.JfOrPop:
			// A function's start address; MoonSharp's field table does not mark it as an address.
			case OpCode.Closure:
				return true;
			default:
				return false;
		}
	}

	// The instructions LoadString just appended (from start), with their symbols and positions; null when
	// something in them cannot be copied safely.
	private static Compiled Capture(Script script, int start, string name, string code)
	{
		List<Instruction> list = script.m_ByteCode.Code;
		int end = list.Count;
		int sourceIdx = script.m_Sources.Count - 1;
		SourceCode source = script.m_Sources[sourceIdx];
		if (end <= start || source.Name != name || !ReferenceEquals(source.Code, code))
		{
			return null;
		}
		Dictionary<SymbolRef, int> symbols = new Dictionary<SymbolRef, int>();
		List<SymbolRef> symbolRefs = new List<SymbolRef>();
		List<Sym> symbolList = new List<Sym>();
		Dictionary<long, int> groups = new Dictionary<long, int>();
		Dictionary<long, List<int>> refsById = new Dictionary<long, List<int>>();
		List<Ref> refs = new List<Ref>();
		Op[] ops = new Op[end - start];
		for (int i = start; i < end; i++)
		{
			Instruction instruction = list[i];
			if ((int)instruction.OpCode > (int)OpCode.Invalid)
			{
				return Reject(name, "unknown opcode " + (int)instruction.OpCode);
			}
			Op op = new Op
			{
				Code = (byte)instruction.OpCode,
				Address = IsAddress(instruction.OpCode),
				NumVal = instruction.NumVal,
				NumVal2 = instruction.NumVal2,
				Name = instruction.Name,
				Symbol = SymbolIndex(instruction.Symbol, symbols, symbolRefs, symbolList),
				Ref = RefIndex(instruction.SourceCodeRef, sourceIdx, groups, refsById, refs)
			};
			if (op.Ref == -2)
			{
				return Reject(name, "a source position outside the file");
			}
			if (op.Address)
			{
				if (op.NumVal < start || op.NumVal >= end)
				{
					return Reject(name, "a code address outside the file");
				}
				op.NumVal -= start;
			}
			DynValue value = instruction.Value;
			if (!value.IsValid)
			{
				if (value.Type != DataType.Nil || value.Object != null || value.Number != 0.0)
				{
					return Reject(name, "an unknown empty value");
				}
				op.ValueType = NoValue;
			}
			else
			{
				switch (value.Type)
				{
					case DataType.Nil:
					case DataType.Void:
						break;
					case DataType.Boolean:
					case DataType.Number:
						op.Number = value.Number;
						break;
					case DataType.String:
						op.String = value.String;
						break;
					default:
						return Reject(name, "a " + value.Type + " value");
				}
				op.ValueType = (byte)value.Type;
			}
			if (instruction.SymbolList != null)
			{
				op.SymbolList = new int[instruction.SymbolList.Length];
				for (int j = 0; j < op.SymbolList.Length; j++)
				{
					op.SymbolList[j] = SymbolIndex(instruction.SymbolList[j], symbols, symbolRefs, symbolList);
				}
			}
			ops[i - start] = op;
		}
		// Environments of global symbols, which the instructions only reach through the symbol.
		for (int i = 0; i < symbolList.Count; i++)
		{
			int env = SymbolIndex(symbolRefs[i].i_Env, symbols, symbolRefs, symbolList);
			Sym sym = symbolList[i];
			sym.Env = env;
			symbolList[i] = sym;
		}
		int[] sourceRefs = new int[source.Refs.Count];
		for (int i = 0; i < sourceRefs.Length; i++)
		{
			sourceRefs[i] = RefIndex(source.Refs[i], sourceIdx, groups, refsById, refs);
			if (sourceRefs[i] == -2)
			{
				return Reject(name, "a source position outside the file");
			}
		}
		return new Compiled
		{
			Name = name,
			Code = code,
			Groups = groups.Count,
			Ops = ops,
			Symbols = symbolList.ToArray(),
			Refs = refs.ToArray(),
			SourceRefs = sourceRefs
		};
	}

	private static Compiled Reject(string name, string why)
	{
		Plugin.Log.LogInfo("Lua compile cache: " + name + " is compiled each time (" + why + ").");
		return null;
	}

	// SymbolRef has no Equals, so the dictionary keeps shared symbols shared, as compiled.
	private static int SymbolIndex(SymbolRef symbol, Dictionary<SymbolRef, int> symbols, List<SymbolRef> refs, List<Sym> list)
	{
		if (symbol == null)
		{
			return -1;
		}
		if (symbols.TryGetValue(symbol, out int index))
		{
			return index;
		}
		index = list.Count;
		symbols[symbol] = index;
		refs.Add(symbol);
		list.Add(new Sym
		{
			DefaultEnv = ReferenceEquals(symbol, SymbolRef.DefaultEnv),
			Type = (byte)symbol.i_Type,
			Index = symbol.i_Index,
			Name = symbol.i_Name,
			Env = -1
		});
		return index;
	}

	// -2: a position from another file, or an unset one that still holds values (it could not be made again).
	private static int RefIndex(SourceRef sourceRef, int sourceIdx, Dictionary<long, int> groups, Dictionary<long, List<int>> byId, List<Ref> refs)
	{
		if (sourceRef.IsClrLocation || (sourceRef.Id != 0 && sourceRef.SourceIdx != sourceIdx) || (sourceRef.Id == 0 && (sourceRef.SourceIdx != 0 || sourceRef.FromLine != 0 || sourceRef.ToLine != 0 || sourceRef.FromChar != 0 || sourceRef.ToChar != 0 || sourceRef.IsStepStop || sourceRef.CannotBreakpoint || sourceRef.Breakpoint)))
		{
			return -2;
		}
		int group = -1;
		if (sourceRef.Id != 0 && !groups.TryGetValue(sourceRef.Id, out group))
		{
			group = groups.Count;
			groups[sourceRef.Id] = group;
		}
		Ref entry = new Ref
		{
			Group = group,
			FromChar = sourceRef.FromChar,
			ToChar = sourceRef.ToChar,
			FromLine = sourceRef.FromLine,
			ToLine = sourceRef.ToLine,
			StepStop = sourceRef.IsStepStop,
			CannotBreakpoint = sourceRef.CannotBreakpoint,
			Breakpoint = sourceRef.Breakpoint
		};
		if (!byId.TryGetValue(sourceRef.Id, out List<int> same))
		{
			same = new List<int>();
			byId[sourceRef.Id] = same;
		}
		foreach (int index in same)
		{
			if (refs[index].Equals(entry))
			{
				return index;
			}
		}
		same.Add(refs.Count);
		refs.Add(entry);
		return refs.Count - 1;
	}

	// Same steps as LoadString after its compile: register the source, append the code, make the closure.
	private static DynValue Restore(Script script, Compiled compiled, Table globalTable)
	{
		script.CheckScriptOwnership(globalTable);
		int sourceIdx = script.m_Sources.Count;
		List<Instruction> list = script.m_ByteCode.Code;
		int start = list.Count;
		// New Ids, one per compiled Id, so positions that were the same stay the same.
		long[] ids = new long[compiled.Groups];
		SourceRef[] refs = new SourceRef[compiled.Refs.Length];
		for (int i = 0; i < refs.Length; i++)
		{
			Ref entry = compiled.Refs[i];
			if (entry.Group < 0)
			{
				refs[i] = default(SourceRef);
				continue;
			}
			SourceRef sourceRef = new SourceRef(sourceIdx, entry.FromChar, entry.ToChar, entry.FromLine, entry.ToLine, entry.StepStop);
			if (ids[entry.Group] == 0)
			{
				ids[entry.Group] = sourceRef.Id;
			}
			else
			{
				sourceRef.Id = ids[entry.Group];
			}
			if (entry.CannotBreakpoint)
			{
				sourceRef = sourceRef.SetNoBreakPoint();
			}
			sourceRef.Breakpoint = entry.Breakpoint;
			refs[i] = sourceRef;
		}
		SymbolRef[] symbols = new SymbolRef[compiled.Symbols.Length];
		for (int i = 0; i < symbols.Length; i++)
		{
			Sym sym = compiled.Symbols[i];
			symbols[i] = sym.DefaultEnv ? SymbolRef.DefaultEnv : new SymbolRef
			{
				i_Type = (SymbolRefType)sym.Type,
				i_Index = sym.Index,
				i_Name = sym.Name
			};
		}
		for (int i = 0; i < symbols.Length; i++)
		{
			Sym sym = compiled.Symbols[i];
			if (!sym.DefaultEnv && sym.Env >= 0)
			{
				symbols[i].i_Env = symbols[sym.Env];
			}
		}
		Instruction[] instructions = new Instruction[compiled.Ops.Length];
		for (int i = 0; i < instructions.Length; i++)
		{
			Op op = compiled.Ops[i];
			Instruction instruction = new Instruction(refs[op.Ref])
			{
				OpCode = (OpCode)op.Code,
				NumVal = op.Address ? op.NumVal + start : op.NumVal,
				NumVal2 = op.NumVal2,
				Name = op.Name,
				Value = ValueOf(op),
				Symbol = (op.Symbol >= 0) ? symbols[op.Symbol] : null
			};
			if (op.SymbolList != null)
			{
				instruction.SymbolList = new SymbolRef[op.SymbolList.Length];
				for (int j = 0; j < op.SymbolList.Length; j++)
				{
					instruction.SymbolList[j] = (op.SymbolList[j] >= 0) ? symbols[op.SymbolList[j]] : null;
				}
			}
			instructions[i] = instruction;
		}
		SourceCode source = new SourceCode(compiled.Name, compiled.Code, sourceIdx, script);
		foreach (int index in compiled.SourceRefs)
		{
			source.Refs.Add(refs[index]);
		}
		script.m_Sources.Add(source);
		list.AddRange(instructions);
		script.SignalSourceCodeChange(source);
		script.SignalByteCodeChange();
		return script.MakeClosure(start, globalTable ?? script.m_GlobalTable);
	}

	private static DynValue ValueOf(Op op)
	{
		switch (op.ValueType)
		{
			case NoValue:
				return default(DynValue);
			case (byte)DataType.Nil:
				return DynValue.Nil;
			case (byte)DataType.Void:
				return DynValue.Void;
			case (byte)DataType.Boolean:
				return (op.Number != 0.0) ? DynValue.True : DynValue.False;
			case (byte)DataType.Number:
				return DynValue.NewNumber(op.Number);
			case (byte)DataType.String:
				return DynValue.NewString(op.String);
			default:
				throw new InvalidDataException("value type " + op.ValueType);
		}
	}

	// One file per Lua file name, replaced when its text (or the game's MoonSharp) changes.
	private static string PathFor(string name)
	{
		StringBuilder hash = new StringBuilder();
		using (SHA1 sha = SHA1.Create())
		{
			foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(name)))
			{
				hash.Append(b.ToString("x2"));
			}
		}
		string file = Path.GetFileNameWithoutExtension(name);
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			file = file.Replace(c, '_');
		}
		return Path.Combine(CacheDir, file + "_" + hash.ToString(0, 16) + ".bin");
	}

	private static void Write(Compiled compiled)
	{
		Directory.CreateDirectory(CacheDir);
		string path = PathFor(compiled.Name);
		string temp = path + ".tmp";
		using (BinaryWriter writer = new BinaryWriter(File.Create(temp)))
		{
			writer.Write(Magic);
			writer.Write(FormatVersion);
			WriteString(writer, Compiler);
			WriteString(writer, compiled.Name);
			WriteString(writer, compiled.Code);
			writer.Write(compiled.Groups);
			writer.Write(compiled.Refs.Length);
			foreach (Ref entry in compiled.Refs)
			{
				writer.Write(entry.Group);
				writer.Write(entry.FromChar);
				writer.Write(entry.ToChar);
				writer.Write(entry.FromLine);
				writer.Write(entry.ToLine);
				writer.Write((byte)((entry.StepStop ? 1 : 0) | (entry.CannotBreakpoint ? 2 : 0) | (entry.Breakpoint ? 4 : 0)));
			}
			writer.Write(compiled.Symbols.Length);
			foreach (Sym sym in compiled.Symbols)
			{
				writer.Write(sym.DefaultEnv);
				writer.Write(sym.Type);
				writer.Write(sym.Index);
				WriteString(writer, sym.Name);
				writer.Write(sym.Env);
			}
			writer.Write(compiled.Ops.Length);
			foreach (Op op in compiled.Ops)
			{
				writer.Write(op.Code);
				writer.Write(op.Address);
				writer.Write(op.NumVal);
				writer.Write(op.NumVal2);
				WriteString(writer, op.Name);
				writer.Write(op.ValueType);
				if (op.ValueType == (byte)DataType.Boolean || op.ValueType == (byte)DataType.Number)
				{
					writer.Write(op.Number);
				}
				else if (op.ValueType == (byte)DataType.String)
				{
					WriteString(writer, op.String);
				}
				writer.Write(op.Symbol);
				writer.Write((op.SymbolList != null) ? op.SymbolList.Length : -1);
				if (op.SymbolList != null)
				{
					foreach (int symbol in op.SymbolList)
					{
						writer.Write(symbol);
					}
				}
				writer.Write(op.Ref);
			}
			writer.Write(compiled.SourceRefs.Length);
			foreach (int index in compiled.SourceRefs)
			{
				writer.Write(index);
			}
			writer.Write(Magic);
		}
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		File.Move(temp, path);
	}

	// Null when there is no cache file for this exact text and MoonSharp build.
	private static Compiled Read(string name, string code)
	{
		string path = PathFor(name);
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			using (BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(path))))
			{
				if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion || ReadString(reader) != Compiler || ReadString(reader) != name || ReadString(reader) != code)
				{
					return null;
				}
				Compiled compiled = new Compiled
				{
					Name = name,
					Code = code,
					Groups = reader.ReadInt32()
				};
				compiled.Refs = new Ref[reader.ReadInt32()];
				for (int i = 0; i < compiled.Refs.Length; i++)
				{
					Ref entry = new Ref
					{
						Group = reader.ReadInt32(),
						FromChar = reader.ReadInt32(),
						ToChar = reader.ReadInt32(),
						FromLine = reader.ReadInt32(),
						ToLine = reader.ReadInt32()
					};
					byte flags = reader.ReadByte();
					entry.StepStop = (flags & 1) != 0;
					entry.CannotBreakpoint = (flags & 2) != 0;
					entry.Breakpoint = (flags & 4) != 0;
					if (entry.Group >= compiled.Groups)
					{
						throw new InvalidDataException("position group");
					}
					compiled.Refs[i] = entry;
				}
				compiled.Symbols = new Sym[reader.ReadInt32()];
				for (int i = 0; i < compiled.Symbols.Length; i++)
				{
					compiled.Symbols[i] = new Sym
					{
						DefaultEnv = reader.ReadBoolean(),
						Type = reader.ReadByte(),
						Index = reader.ReadInt32(),
						Name = ReadString(reader),
						Env = reader.ReadInt32()
					};
					if (compiled.Symbols[i].Env >= compiled.Symbols.Length)
					{
						throw new InvalidDataException("symbol environment");
					}
				}
				compiled.Ops = new Op[reader.ReadInt32()];
				for (int i = 0; i < compiled.Ops.Length; i++)
				{
					Op op = new Op
					{
						Code = reader.ReadByte(),
						Address = reader.ReadBoolean(),
						NumVal = reader.ReadInt32(),
						NumVal2 = reader.ReadInt32(),
						Name = ReadString(reader),
						ValueType = reader.ReadByte()
					};
					if (op.ValueType == (byte)DataType.Boolean || op.ValueType == (byte)DataType.Number)
					{
						op.Number = reader.ReadDouble();
					}
					else if (op.ValueType == (byte)DataType.String)
					{
						op.String = ReadString(reader);
					}
					op.Symbol = reader.ReadInt32();
					int count = reader.ReadInt32();
					if (count >= 0)
					{
						op.SymbolList = new int[count];
						for (int j = 0; j < count; j++)
						{
							op.SymbolList[j] = reader.ReadInt32();
						}
					}
					op.Ref = reader.ReadInt32();
					if (op.Code > (byte)OpCode.Invalid || op.Ref < 0 || op.Ref >= compiled.Refs.Length || op.Symbol >= compiled.Symbols.Length || (op.Address && (op.NumVal < 0 || op.NumVal >= compiled.Ops.Length)))
					{
						throw new InvalidDataException("instruction " + i);
					}
					compiled.Ops[i] = op;
				}
				compiled.SourceRefs = new int[reader.ReadInt32()];
				for (int i = 0; i < compiled.SourceRefs.Length; i++)
				{
					compiled.SourceRefs[i] = reader.ReadInt32();
					if (compiled.SourceRefs[i] < 0 || compiled.SourceRefs[i] >= compiled.Refs.Length)
					{
						throw new InvalidDataException("source position");
					}
				}
				if (reader.ReadUInt32() != Magic)
				{
					throw new InvalidDataException("end marker");
				}
				return compiled;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("Lua compile cache: " + path + " is damaged, compiling " + name + " again: " + ex.Message);
			return null;
		}
	}

	// Strings as UTF-16 code units, so any Lua string (even a lone surrogate from an escape) comes back the same.
	private static void WriteString(BinaryWriter writer, string text)
	{
		if (text == null)
		{
			writer.Write(-1);
			return;
		}
		writer.Write(text.Length);
		foreach (char c in text)
		{
			writer.Write((ushort)c);
		}
	}

	private static string ReadString(BinaryReader reader)
	{
		int length = reader.ReadInt32();
		if (length < 0)
		{
			return null;
		}
		char[] chars = new char[length];
		for (int i = 0; i < length; i++)
		{
			chars[i] = (char)reader.ReadUInt16();
		}
		return new string(chars);
	}
}
