using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;

namespace CastleStoryPlus.Core;

// Development: loads the newest build of the mod (CastleStoryPlus.dll) into the running game, without a restart
// (DevTools POST /api/mod/reload). Mono cannot unload an assembly, so the build is loaded as a new copy under a name
// of its own (renamed with Mono.Cecil, as UiAssembly does for the menu). Before that the current copy is taken out:
// its features' static Disable() runs, every Harmony patch of the mod is removed and its MonoBehaviours on the plugin
// object are destroyed (an open in-game menu is closed and opened again from the new copy). The new copy's Plugin
// then binds the settings and patches its features as at startup. The old copy's components on the game's objects
// (an ore mine's goal keeper, the minimap's terrain) are swapped for the new copy's type of the same name, their
// fields copied where the types still fit.
// What stays from the old copy: its signal and event handlers (unless its Disable() takes them away), its static
// data and threads, and everything DevTools and the tests reach (they are bound to the startup copy).
internal static class ModReload
{
	private const string PluginType = "CastleStoryPlus.Plugin";

	private static readonly string[] HarmonyIds = { Plugin.Guid, Plugin.Guid + ".performance", Plugin.Guid + ".performance.mod" };

	// The startup copy's file, and the copy whose features are running now.
	private static string _path;

	private static Assembly _current;

	private static bool _resolving;

	// How many times the mod was reloaded.
	internal static int Reloads { get; private set; }

	internal static Assembly Current
	{
		get { return _current; }
	}

	// The startup copy, from its Awake; a reloaded copy's Awake never runs.
	internal static void Remember(Assembly assembly, string path)
	{
		if (_current == null)
		{
			_current = assembly;
			_path = path;
		}
	}

	// Answers what was done; throws when the new build could not be loaded (the old copy is then already out).
	internal static string Reload()
	{
		if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
		{
			throw new InvalidOperationException("the mod's file is not known or missing: " + _path);
		}
		if (!_resolving)
		{
			// A reloaded menu assembly names the copy it was built against; Mono finds a copy loaded from bytes only here.
			AppDomain.CurrentDomain.AssemblyResolve += Resolve;
			_resolving = true;
		}
		// The new copy is made first: when that fails, the running copy is left as it is.
		byte[] bytes = Renamed(_path);
		Assembly old = _current;
		bool menuOpen = CloseMenu(old);
		int disabled = DisableFeatures(old);
		foreach (string id in HarmonyIds)
		{
			new Harmony(id).UnpatchSelf();
		}
		int destroyed = DestroyComponents(old);
		Assembly loaded = Assembly.Load(bytes);
		Type plugin = loaded.GetType(PluginType, true);
		plugin.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Plugin.Log, Plugin.Cfg, Plugin.Root, Plugin.Directory });
		_current = loaded;
		Reloads++;
		int swapped = SwapComponents(old, loaded);
		if (menuOpen)
		{
			OpenMenu(loaded);
		}
		string built = File.GetLastWriteTime(_path).ToString("dd-MM-yyyy HH:mm:ss");
		string result = "reload " + Reloads + ": " + loaded.GetName().Name + " (built " + built + "); old copy: " + disabled + " features disabled, " + destroyed + " components destroyed, " + swapped + " swapped" + (menuOpen ? ", menu opened again" : string.Empty);
		Plugin.Log.LogInfo("ModReload: " + result);
		return result;
	}

	// The file as an assembly of a name of its own. Writing it resolves its references (the game's assemblies), so
	// Cecil looks in the folders of every assembly loaded with a file.
	private static byte[] Renamed(string path)
	{
		DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
		HashSet<string> folders = new HashSet<string>();
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			string location;
			try
			{
				location = assembly.Location;
			}
			catch (NotSupportedException)
			{
				continue;
			}
			if (!string.IsNullOrEmpty(location) && folders.Add(Path.GetDirectoryName(location)))
			{
				resolver.AddSearchDirectory(Path.GetDirectoryName(location));
			}
		}
		using (AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), new ReaderParameters { AssemblyResolver = resolver }))
		{
			definition.Name.Name = "CastleStoryPlus-" + DateTime.Now.Ticks;
			using (MemoryStream renamed = new MemoryStream())
			{
				definition.Write(renamed);
				return renamed.ToArray();
			}
		}
	}

	// Calls a static method without parameters in the copy now running (DevTools is bound to the startup copy).
	internal static object CallCurrent(string type, string method)
	{
		return _current.GetType(type, true).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null, null);
	}

	private static Assembly Resolve(object sender, ResolveEventArgs args)
	{
		string name = new AssemblyName(args.Name).Name;
		return _current != null && name == _current.GetName().Name ? _current : null;
	}

	// Calls each feature's static Disable() in the copy.
	private static int DisableFeatures(Assembly assembly)
	{
		int count = 0;
		foreach (Type type in assembly.GetTypes())
		{
			MethodInfo disable = type.GetMethod("Disable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (disable == null || !Attribute.IsDefined(type, assembly.GetType(typeof(FeatureAttribute).FullName)))
			{
				continue;
			}
			try
			{
				disable.Invoke(null, null);
				count++;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("ModReload: " + type.Name + ".Disable: " + ex);
			}
		}
		return count;
	}

	// The copy's MonoBehaviours on the plugin object, except the plugin itself.
	private static int DestroyComponents(Assembly assembly)
	{
		int count = 0;
		foreach (MonoBehaviour behaviour in Plugin.Root.GetComponents<MonoBehaviour>())
		{
			if (behaviour != null && behaviour.GetType().Assembly == assembly && !(behaviour is BaseUnityPlugin))
			{
				UnityEngine.Object.Destroy(behaviour);
				count++;
			}
		}
		return count;
	}

	// The old copy's components on the game's objects become the new copy's type of the same name (gone when it has
	// none), with the fields whose types still fit copied over.
	private static int SwapComponents(Assembly old, Assembly loaded)
	{
		int count = 0;
		foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
		{
			if (behaviour == null || behaviour.GetType().Assembly != old || behaviour is BaseUnityPlugin || behaviour.gameObject.scene.name == null)
			{
				continue;
			}
			Type type = loaded.GetType(behaviour.GetType().FullName);
			if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type))
			{
				try
				{
					Component swap = behaviour.gameObject.AddComponent(type);
					CopyFields(behaviour, swap);
					((Behaviour)swap).enabled = behaviour.enabled;
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning("ModReload: " + type.Name + " on " + behaviour.name + " not swapped: " + ex.Message);
				}
			}
			UnityEngine.Object.Destroy(behaviour);
			count++;
		}
		return count;
	}

	private static void CopyFields(object from, object to)
	{
		const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		for (Type source = from.GetType(), target = to.GetType(); source != null && target != null && source.Assembly == from.GetType().Assembly; source = source.BaseType, target = target.BaseType)
		{
			foreach (FieldInfo field in source.GetFields(Fields))
			{
				FieldInfo other = target.GetField(field.Name, Fields);
				if (other == null || other.IsInitOnly)
				{
					continue;
				}
				object value = field.GetValue(from);
				if (value == null ? !other.FieldType.IsValueType : other.FieldType.IsInstanceOfType(value))
				{
					other.SetValue(to, value);
				}
			}
		}
	}

	// The copy's in-game menu (UiAssembly): whether one was open; it is closed.
	private static bool CloseMenu(Assembly assembly)
	{
		try
		{
			Type ui = assembly.GetType("CastleStoryPlus.GameUi.UiAssembly");
			Delegate model = (Delegate)ui?.GetField("Model", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
			if (model == null || model.DynamicInvoke() == null)
			{
				return false;
			}
			((Delegate)ui.GetField("Close", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.DynamicInvoke();
			return true;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("ModReload: closing the menu: " + ex);
			return false;
		}
	}

	private static void OpenMenu(Assembly assembly)
	{
		try
		{
			Type ui = assembly.GetType("CastleStoryPlus.GameUi.UiAssembly");
			((Delegate)ui?.GetField("Open", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.DynamicInvoke(true);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError("ModReload: opening the menu: " + ex);
		}
	}
}
