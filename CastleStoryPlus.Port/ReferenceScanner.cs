using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CastleStoryPlus.Port;

// Lists the types and members the game's assemblies use that the Unity 6 runtime no longer has: what CodePatcher
// has to redirect. Resolves against the given directories, first match wins (Unity 6's assemblies before the game's).
public sealed class ReferenceScanner
{
	private readonly DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();

	public ReferenceScanner(IEnumerable<string> searchDirs)
	{
		foreach (string dir in resolver.GetSearchDirectories())
		{
			resolver.RemoveSearchDirectory(dir);
		}
		foreach (string dir in searchDirs)
		{
			resolver.AddSearchDirectory(dir);
		}
	}

	public IAssemblyResolver Resolver => resolver;

	// Missing references of one assembly: "type" or "member" lines with the methods using them.
	public SortedDictionary<string, SortedSet<string>> Scan(string assemblyPath)
	{
		var missing = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
		using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { AssemblyResolver = resolver }))
		{
			ModuleDefinition module = assembly.MainModule;
			foreach (TypeDefinition type in module.GetTypes())
			{
				if (type.BaseType != null)
				{
					CheckType(type.BaseType, type.FullName, missing);
				}
				foreach (FieldDefinition field in type.Fields)
				{
					CheckType(field.FieldType, type.FullName, missing);
				}
				foreach (MethodDefinition method in type.Methods)
				{
					if (!method.HasBody)
					{
						continue;
					}
					string where = type.FullName + "::" + method.Name;
					foreach (Instruction instruction in method.Body.Instructions)
					{
						switch (instruction.Operand)
						{
							case MethodReference methodRef:
								CheckMember(methodRef, where, missing);
								break;
							case FieldReference fieldRef:
								CheckMember(fieldRef, where, missing);
								break;
							case TypeReference typeRef:
								CheckType(typeRef, where, missing);
								break;
						}
					}
				}
			}
		}
		return missing;
	}

	private static void CheckType(TypeReference type, string where, SortedDictionary<string, SortedSet<string>> missing)
	{
		TypeReference element = type.GetElementType();
		if (element.IsGenericParameter || element.Scope is ModuleDefinition)
		{
			return;
		}
		TypeDefinition resolved;
		try
		{
			resolved = element.Resolve();
		}
		catch (AssemblyResolutionException e)
		{
			Add(missing, "assembly " + e.AssemblyReference.Name, where);
			return;
		}
		if (resolved == null)
		{
			Add(missing, "type " + element.FullName + " [" + element.Scope.Name + "]", where);
		}
	}

	private static void CheckMember(MemberReference member, string where, SortedDictionary<string, SortedSet<string>> missing)
	{
		TypeReference declaring = member.DeclaringType;
		if (declaring == null)
		{
			return;
		}
		// Array methods (Get, Set, .ctor of T[,]) are made by the runtime.
		if (declaring is ArrayType)
		{
			return;
		}
		TypeReference element = declaring.GetElementType();
		if (element.Scope is ModuleDefinition)
		{
			return;
		}
		CheckType(declaring, where, missing);
		IMemberDefinition resolved;
		try
		{
			resolved = member switch
			{
				MethodReference m => m.Resolve(),
				FieldReference f => f.Resolve(),
				_ => null,
			};
		}
		catch (AssemblyResolutionException)
		{
			return;
		}
		if (resolved == null && element.Resolve() != null)
		{
			Add(missing, "member " + member.FullName + " [" + element.Scope.Name + "]", where);
		}
	}

	private static void Add(SortedDictionary<string, SortedSet<string>> missing, string what, string where)
	{
		if (!missing.TryGetValue(what, out SortedSet<string> users))
		{
			users = new SortedSet<string>(StringComparer.Ordinal);
			missing[what] = users;
		}
		users.Add(where);
	}
}
