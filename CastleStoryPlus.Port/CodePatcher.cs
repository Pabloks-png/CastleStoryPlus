using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CastleStoryPlus.Port;

// Patches the game's assemblies (built against Unity 5.6) to run on the Unity 6 player:
//  - engine types Unity 6 no longer has move to CastleStoryPlus.Compat (same full names, see the player's Compat);
//  - renamed engine types get their new name;
//  - calls to members Unity 6 removed or changed go to a replacement (Unity's own, or Compat's Redirects).
// Runs on the player's own copy of the game's assemblies; nothing patched is distributed.
public sealed class CodePatcher
{
	public const string CompatAssembly = "CastleStoryPlus.Compat";
	private const string Redirects = "CastleStoryPlus.Compat.Redirects";

	private static readonly HashSet<string> MovedToCompat = new HashSet<string>(StringComparer.Ordinal)
	{
		"UnityEngine.Network",
		"UnityEngine.NetworkPlayer",
		"UnityEngine.NetworkMessageInfo",
		"UnityEngine.NetworkDisconnection",
		"UnityEngine.BitStream",
		"UnityEngine.GUIElement",
		"UnityEngine.GUIText",
		"UnityEngine.GUITexture",
		"UnityEngine.Networking.ChannelQOS",
		"UnityEngine.Networking.ConnectionAcksType",
		"UnityEngine.Networking.ConnectionConfig",
		"UnityEngine.Networking.ConnectionSimulatorConfig",
		"UnityEngine.Networking.GlobalConfig",
		"UnityEngine.Networking.HostTopology",
		"UnityEngine.Networking.NetworkError",
		"UnityEngine.Networking.NetworkEventType",
		"UnityEngine.Networking.NetworkTransport",
		"UnityEngine.Networking.QosType",
		"UnityEngine.Networking.Utility",
		"UnityEngine.Networking.Match.MatchInfo",
		"UnityEngine.Networking.Match.MatchInfoSnapshot",
		"UnityEngine.Networking.Match.NetworkMatch",
		"UnityEngine.Networking.Types.NetworkID",
		"UnityEngine.Networking.Types.NodeID",
		"UnityEngine.Networking.Types.SourceID",
	};

	private static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "UnityEngine.PhysicMaterial", "PhysicsMaterial" },
		{ "UnityEngine.PhysicMaterialCombine", "PhysicsMaterialCombine" },
	};

	// "declaring type::name(parameter types)" -> (assembly, type, method). The replacement is static and takes the
	// instance first.
	private static readonly Dictionary<string, (string Assembly, string Type, string Method)> CallRedirects = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
	{
		{ "UnityEngine.Texture2D::LoadImage(System.Byte[])", ("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "LoadImage") },
		{ "UnityEngine.Texture2D::LoadImage(System.Byte[],System.Boolean)", ("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "LoadImage") },
		{ "UnityEngine.Texture2D::EncodeToPNG()", ("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "EncodeToPNG") },
		{ "UnityEngine.Texture2D::EncodeToJPG()", ("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "EncodeToJPG") },
		{ "UnityEngine.Texture2D::EncodeToJPG(System.Int32)", ("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion", "EncodeToJPG") },
		{ "UnityEngine.SceneManagement.SceneManager::LoadScene(System.Int32)", (CompatAssembly, Redirects, "SceneManager_LoadScene") },
		{ "UnityEngine.SceneManagement.SceneManager::LoadScene(System.Int32,UnityEngine.SceneManagement.LoadSceneMode)", (CompatAssembly, Redirects, "SceneManager_LoadScene") },
		{ "UnityEngine.SceneManagement.SceneManager::LoadSceneAsync(System.Int32)", (CompatAssembly, Redirects, "SceneManager_LoadSceneAsync") },
		{ "UnityEngine.SceneManagement.SceneManager::LoadSceneAsync(System.Int32,UnityEngine.SceneManagement.LoadSceneMode)", (CompatAssembly, Redirects, "SceneManager_LoadSceneAsync") },
		{ "UnityEngine.SceneManagement.Scene::get_buildIndex()", (CompatAssembly, Redirects, "Scene_get_buildIndex") },
		{ "UnityEngine.Camera::get_hdr()", (CompatAssembly, Redirects, "Camera_get_hdr") },
		{ "UnityEngine.Camera::set_hdr(System.Boolean)", (CompatAssembly, Redirects, "Camera_set_hdr") },
		{ "UnityEngine.Graphics::DrawProceduralIndirect(UnityEngine.MeshTopology,UnityEngine.ComputeBuffer,System.Int32)", (CompatAssembly, Redirects, "Graphics_DrawProceduralIndirect") },
		{ "UnityEngine.UI.InputField::get_onEndEdit()", (CompatAssembly, Redirects, "InputField_get_onEndEdit") },
		{ "UnityEngine.UI.InputField::set_onEndEdit(UnityEngine.UI.InputField/SubmitEvent)", (CompatAssembly, Redirects, "InputField_set_onEndEdit") },
		{ "UnityEngine.GameObject::AddComponent(System.Type)", (CompatAssembly, Redirects, "GameObject_AddComponent") },
	};

	// Generic methods redirected whatever their type arguments: "declaring type::name<>(parameter types)", with a comma
	// per further type argument. The replacement is a generic method with as many type parameters, called with the
	// same type arguments.
	private static readonly Dictionary<string, (string Assembly, string Type, string Method)> GenericRedirects = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
	{
		{ "UnityEngine.GameObject::AddComponent<>()", (CompatAssembly, Redirects, "GameObject_AddComponent") },
	};

	// Calls redirected only inside one method: "caller type::method callee key", as in CallRedirects.
	private static readonly Dictionary<string, (string Assembly, string Type, string Method)> ScopedRedirects = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
	{
		{ "Brix.Game.ScreenSignaler::InitPlaneGetter System.Delegate::CreateDelegate(System.Type,System.Reflection.MethodInfo)", (CompatAssembly, Redirects, "ScreenSignaler_CreateDelegate") },
		{ "Brix.Util.Logging.UnityLogAppender::BindDefaultLogger System.Delegate::CreateDelegate(System.Type,System.Reflection.MethodInfo)", (CompatAssembly, Redirects, "UnityLogAppender_CreateDelegate") },
		{ "Brix.Lua.LuaProperty`2::.ctor System.Delegate::CreateDelegate(System.Type,System.Object,System.Reflection.MethodInfo)", (CompatAssembly, Redirects, "LuaProperty_CreateDelegate") },
		{ "Brix.Lua.LuaDriverHQ::InitInfoList System.Reflection.Assembly::GetTypes()", (CompatAssembly, Redirects, "LuaDriverHQ_GetTypes") },
		{ "Brix.External.Factories.Templates.CloneTemplate::Instantiate UnityEngine.Object::Instantiate<UnityEngine.GameObject>(!!0,UnityEngine.Vector3,UnityEngine.Quaternion)", (CompatAssembly, Redirects, "CloneTemplate_Instantiate") },
	};

	// Methods that worker threads call at the same time and that fill a Dictionary without a lock. Mono 2.6's Dictionary
	// mostly got away with it; the current one throws, and the method fails. They are made synchronized (a lock on the
	// instance).
	private static readonly HashSet<string> Synchronized = new HashSet<string>(StringComparer.Ordinal)
	{
		// The distant terrain's noise colours, per width (LodUpdater's drawers).
		"Brix.Engine.FlatWorld.Common::GetProperlySizedNoiseTexColors",
	};

	// String literals naming runtime types or assemblies by a name that changed.
	private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		// Mono's internal reflection types were renamed (MonoMethod is RuntimeMethodInfo in Unity 6's Mono).
		{ "System.Reflection.MonoMethod", "System.Reflection.RuntimeMethodInfo" },
		// The game loads brix-common.dll by a name it never had.
		{ "Brix.Common, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", "brix-common, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null" },
	};

	private readonly GameResolver resolver;
	private readonly Dictionary<string, AssemblyDefinition> targets = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

	// searchDirs: the player's Managed folder (Unity 6, Compat) first, then the game's. The game's own assemblies
	// (gameAssemblies, by name) are always read from gameDir, never from an earlier install's patched copies in the
	// player, so every install writes the same files.
	public CodePatcher(IEnumerable<string> searchDirs, string gameDir, IEnumerable<string> gameAssemblies)
	{
		resolver = new GameResolver(gameDir, gameAssemblies);
		foreach (string dir in resolver.GetSearchDirectories())
		{
			resolver.RemoveSearchDirectory(dir);
		}
		foreach (string dir in searchDirs)
		{
			resolver.AddSearchDirectory(dir);
		}
	}

	private sealed class GameResolver : DefaultAssemblyResolver
	{
		private readonly string gameDir;
		private readonly HashSet<string> gameAssemblies;
		private readonly Dictionary<string, AssemblyDefinition> read = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

		public GameResolver(string gameDir, IEnumerable<string> gameAssemblies)
		{
			this.gameDir = gameDir;
			this.gameAssemblies = new HashSet<string>(gameAssemblies, StringComparer.Ordinal);
		}

		public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			if (!gameAssemblies.Contains(name.Name))
			{
				return base.Resolve(name, parameters);
			}
			if (!read.TryGetValue(name.Name, out AssemblyDefinition assembly))
			{
				parameters ??= new ReaderParameters();
				parameters.AssemblyResolver ??= this;
				assembly = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, name.Name + ".dll"), parameters);
				read[name.Name] = assembly;
			}
			return assembly;
		}

		protected override void Dispose(bool disposing)
		{
			foreach (AssemblyDefinition assembly in read.Values)
			{
				assembly.Dispose();
			}
			read.Clear();
			base.Dispose(disposing);
		}
	}

	public Action<string> Log { get; set; } = _ => { };

	public void Patch(string source, string target)
	{
		// Read fully into memory: the target may be in a folder the resolver also searches.
		byte[] bytes = File.ReadAllBytes(source);
		using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver }))
		{
			ModuleDefinition module = assembly.MainModule;
			int types = RetargetTypes(module);
			int calls = RedirectCalls(module);
			int strings = ReplaceStrings(module);
			int layouts = FixGenericUnions(module);
			int locks = Synchronize(module);
			// Written next to it and renamed: on a second install the running player has the old one loaded.
			assembly.Write(target + ".tmp");
			Installer.MoveInto(target + ".tmp", target);
			Log(Path.GetFileName(source) + ": " + types + " type references, " + calls + " calls, " + strings + " strings, " + layouts + " layouts, " + locks + " locks patched");
		}
	}

	private static int RetargetTypes(ModuleDefinition module)
	{
		AssemblyNameReference compat = null;
		int count = 0;
		foreach (TypeReference type in module.GetTypeReferences())
		{
			if (type.DeclaringType != null || type.Scope?.Name != "UnityEngine")
			{
				continue;
			}
			if (MovedToCompat.Contains(type.FullName))
			{
				if (compat == null)
				{
					compat = module.AssemblyReferences.FirstOrDefault(r => r.Name == CompatAssembly);
					if (compat == null)
					{
						compat = new AssemblyNameReference(CompatAssembly, new Version(0, 0, 0, 0));
						module.AssemblyReferences.Add(compat);
					}
				}
				type.Scope = compat;
				count++;
			}
			else if (Renamed.TryGetValue(type.FullName, out string name))
			{
				type.Name = name;
				count++;
			}
		}
		return count;
	}

	// Mono no longer loads a generic type with explicit layout. The game has two (Smooth's blittable equality comparers,
	// which compare T through a T/int union); they get a sequential layout and compare through
	// System.Collections.Generic.EqualityComparer<T>.Default, which gives the same answers.
	private int FixGenericUnions(ModuleDefinition module)
	{
		int count = 0;
		foreach (TypeDefinition type in module.GetTypes().ToList())
		{
			if (!type.IsExplicitLayout || !(type.HasGenericParameters || (type.DeclaringType?.HasGenericParameters ?? false)))
			{
				continue;
			}
			TypeDefinition comparer = type.DeclaringType;
			if (comparer == null || comparer.GenericParameters.Count != 1)
			{
				throw new InvalidDataException("No fix for the explicit layout of " + type.FullName);
			}
			type.IsExplicitLayout = false;
			type.IsSequentialLayout = true;
			foreach (FieldDefinition field in type.Fields)
			{
				field.Offset = -1;
			}
			RewriteWithDefaultComparer(module, comparer);
			count++;
		}
		return count;
	}

	private void RewriteWithDefaultComparer(ModuleDefinition module, TypeDefinition comparer)
	{
		TypeDefinition systemComparer = resolver.Resolve(new AssemblyNameReference("mscorlib", null)).MainModule.GetType("System.Collections.Generic.EqualityComparer`1");
		TypeReference open = module.ImportReference(systemComparer);
		// Members are referenced on EqualityComparer<T of the comparer>, with signatures in the class's own !0.
		var instance = new GenericInstanceType(open);
		instance.GenericArguments.Add(comparer.GenericParameters[0]);
		GenericParameter genericT = open.GenericParameters[0];
		var self = new GenericInstanceType(open);
		self.GenericArguments.Add(genericT);
		var getDefault = new MethodReference("get_Default", self, instance) { HasThis = false };
		var equals = new MethodReference("Equals", module.TypeSystem.Boolean, instance) { HasThis = true };
		equals.Parameters.Add(new ParameterDefinition(genericT));
		equals.Parameters.Add(new ParameterDefinition(genericT));
		var hash = new MethodReference("GetHashCode", module.TypeSystem.Int32, instance) { HasThis = true };
		hash.Parameters.Add(new ParameterDefinition(genericT));
		foreach (MethodDefinition method in comparer.Methods)
		{
			bool isEquals = method.Name == "Equals" && method.Parameters.Count == 2;
			bool isHash = method.Name == "GetHashCode" && method.Parameters.Count == 1;
			if (!isEquals && !isHash)
			{
				continue;
			}
			method.Body = new MethodBody(method);
			ILProcessor il = method.Body.GetILProcessor();
			il.Emit(OpCodes.Call, getDefault);
			il.Emit(OpCodes.Ldarg_1);
			if (isEquals)
			{
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Callvirt, equals);
			}
			else
			{
				il.Emit(OpCodes.Callvirt, hash);
			}
			il.Emit(OpCodes.Ret);
		}
	}

	private static int Synchronize(ModuleDefinition module)
	{
		int count = 0;
		foreach (TypeDefinition type in module.GetTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (Synchronized.Contains(type.FullName + "::" + method.Name))
				{
					method.ImplAttributes |= MethodImplAttributes.Synchronized;
					count++;
				}
			}
		}
		return count;
	}

	private static int ReplaceStrings(ModuleDefinition module)
	{
		int count = 0;
		foreach (TypeDefinition type in module.GetTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (!method.HasBody)
				{
					continue;
				}
				foreach (Instruction instruction in method.Body.Instructions)
				{
					if (instruction.OpCode == OpCodes.Ldstr && Strings.TryGetValue((string)instruction.Operand, out string replacement))
					{
						instruction.Operand = replacement;
						count++;
					}
				}
			}
		}
		return count;
	}

	private int RedirectCalls(ModuleDefinition module)
	{
		int count = 0;
		var imported = new Dictionary<string, MethodReference>(StringComparer.Ordinal);
		foreach (TypeDefinition type in module.GetTypes())
		{
			foreach (MethodDefinition method in type.Methods)
			{
				if (!method.HasBody)
				{
					continue;
				}
				foreach (Instruction instruction in method.Body.Instructions)
				{
					if (!(instruction.Operand is MethodReference called) || called.DeclaringType == null)
					{
						continue;
					}
					string key = Key(called);
					string scoped = type.FullName + "::" + method.Name + " " + key;
					bool generic = false;
					if (ScopedRedirects.TryGetValue(scoped, out var redirect))
					{
						key = scoped;
					}
					else if (!CallRedirects.TryGetValue(key, out redirect))
					{
						if (!(called is GenericInstanceMethod) || !GenericRedirects.TryGetValue(OpenKey(called), out redirect))
						{
							continue;
						}
						generic = true;
					}
					if (!imported.TryGetValue(key, out MethodReference replacement))
					{
						replacement = module.ImportReference(FindReplacement(called, redirect, generic));
						if (generic)
						{
							var instance = new GenericInstanceMethod(replacement);
							foreach (TypeReference argument in ((GenericInstanceMethod)called).GenericArguments)
							{
								instance.GenericArguments.Add(argument);
							}
							replacement = instance;
						}
						imported[key] = replacement;
					}
					instruction.OpCode = OpCodes.Call;
					instruction.Operand = replacement;
					count++;
				}
			}
		}
		return count;
	}

	private MethodDefinition FindReplacement(MethodReference called, (string Assembly, string Type, string Method) redirect, bool generic)
	{
		if (!targets.TryGetValue(redirect.Assembly, out AssemblyDefinition assembly))
		{
			assembly = resolver.Resolve(new AssemblyNameReference(redirect.Assembly, null));
			targets[redirect.Assembly] = assembly;
		}
		TypeDefinition type = assembly.MainModule.GetType(redirect.Type) ?? throw new InvalidDataException(redirect.Type + " not in " + redirect.Assembly);
		var expected = new List<string>();
		if (called.HasThis)
		{
			expected.Add(called.DeclaringType.FullName);
		}
		expected.AddRange(called.Parameters.Select(p => ParameterTypeName(called, p.ParameterType)));
		foreach (MethodDefinition candidate in type.Methods)
		{
			if (!candidate.IsStatic || candidate.Name != redirect.Method || candidate.Parameters.Count != expected.Count
				|| candidate.GenericParameters.Count != (generic ? ((GenericInstanceMethod)called).GenericArguments.Count : 0))
			{
				continue;
			}
			bool match = true;
			for (int i = 0; i < expected.Count; i++)
			{
				TypeReference parameter = candidate.Parameters[i].ParameterType;
				if (parameter is ByReferenceType byRef)
				{
					parameter = byRef.ElementType;
				}
				if (parameter.FullName != expected[i])
				{
					match = false;
					break;
				}
			}
			if (match)
			{
				return candidate;
			}
		}
		throw new InvalidDataException("No replacement " + redirect.Type + "::" + redirect.Method + " for " + Key(called));
	}

	// A generic method's call is named with its type arguments: "Object::Instantiate<UnityEngine.GameObject>(!!0,...)".
	private static string Key(MethodReference method)
	{
		string arguments = method is GenericInstanceMethod generic ? "<" + string.Join(",", generic.GenericArguments.Select(a => a.FullName)) + ">" : "";
		return method.DeclaringType.FullName + "::" + method.Name + arguments + "(" + string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName)) + ")";
	}

	// A generic method's call without its type arguments: "GameObject::AddComponent<>()".
	private static string OpenKey(MethodReference method)
	{
		var generic = (GenericInstanceMethod)method;
		return method.DeclaringType.FullName + "::" + method.Name + "<" + new string(',', generic.GenericArguments.Count - 1) + ">(" + string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName)) + ")";
	}

	// A parameter's type with the call's generic arguments filled in.
	private static string ParameterTypeName(MethodReference called, TypeReference type)
	{
		if (type is GenericParameter parameter && parameter.Type == GenericParameterType.Method && called is GenericInstanceMethod generic)
		{
			return generic.GenericArguments[parameter.Position].FullName;
		}
		return type.FullName;
	}
}
