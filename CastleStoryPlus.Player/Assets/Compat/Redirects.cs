using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CastleStoryPlus.Compat
{
	// Static stand-ins for engine members the game calls that Unity 6 removed or that behave differently with the
	// game's scenes in bundles. CodePatcher rewrites each call to the method named <Type>_<member>; an instance member
	// becomes a static method taking the instance first (by ref for a struct).
	public static class Redirects
	{
		private static readonly ConditionalWeakTable<InputField, InputField.SubmitEvent> endEditEvents = new ConditionalWeakTable<InputField, InputField.SubmitEvent>();

		// The game's scenes come from bundles and have no build index of their own; the game's build order is the
		// catalog's.
		public static void SceneManager_LoadScene(int sceneBuildIndex)
		{
			SceneManager.LoadScene(GameContent.Scenes[sceneBuildIndex]);
		}

		public static void SceneManager_LoadScene(int sceneBuildIndex, LoadSceneMode mode)
		{
			SceneManager.LoadScene(GameContent.Scenes[sceneBuildIndex], mode);
		}

		public static AsyncOperation SceneManager_LoadSceneAsync(int sceneBuildIndex)
		{
			return SceneManager.LoadSceneAsync(GameContent.Scenes[sceneBuildIndex]);
		}

		public static AsyncOperation SceneManager_LoadSceneAsync(int sceneBuildIndex, LoadSceneMode mode)
		{
			return SceneManager.LoadSceneAsync(GameContent.Scenes[sceneBuildIndex], mode);
		}

		public static int Scene_get_buildIndex(ref Scene scene)
		{
			int index = GameContent.Scenes.IndexOf(scene.name);
			return index >= 0 ? index : scene.buildIndex;
		}

		public static bool Camera_get_hdr(Camera camera)
		{
			return camera.allowHDR;
		}

		public static void Camera_set_hdr(Camera camera, bool value)
		{
			camera.allowHDR = value;
		}

		// 5.6's DrawProceduralIndirect drew at once; that is DrawProceduralIndirectNow now.
		public static void Graphics_DrawProceduralIndirect(MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset)
		{
			Graphics.DrawProceduralIndirectNow(topology, bufferWithArgs, argsOffset);
		}

		// Unity 6's InputField.onEndEdit is an EndEditEvent; the game reads and assigns a SubmitEvent. The game gets a
		// SubmitEvent of its own per field that the real event invokes.
		public static InputField.SubmitEvent InputField_get_onEndEdit(InputField field)
		{
			if (!endEditEvents.TryGetValue(field, out InputField.SubmitEvent submit))
			{
				submit = new InputField.SubmitEvent();
				InputField.SubmitEvent target = submit;
				field.onEndEdit.AddListener(value => target.Invoke(value));
				endEditEvents.Add(field, submit);
			}
			return submit;
		}

		public static void InputField_set_onEndEdit(InputField field, InputField.SubmitEvent value)
		{
			endEditEvents.Remove(field);
			endEditEvents.Add(field, value ?? new InputField.SubmitEvent());
			field.onEndEdit.RemoveAllListeners();
			InputField.SubmitEvent target = value;
			if (target != null)
			{
				field.onEndEdit.AddListener(text => target.Invoke(text));
			}
		}

		// Unity 6's player does not add what a game script's [RequireComponent] asks for when the game adds the script
		// itself (the game's assemblies are not part of the player's build): PlacedBlockFactoryAssetCreator adds a
		// VoxelTransform, a NetworkBehaviour, which on 5.6 brought its NetworkIdentity along, and without one every
		// placed block threw in NetworkBehaviour.SetDirtyBit. The required components are added first, as Unity does.
		public static T GameObject_AddComponent<T>(GameObject gameObject) where T : Component
		{
			AddRequired(gameObject, typeof(T));
			return gameObject.AddComponent<T>();
		}

		public static Component GameObject_AddComponent(GameObject gameObject, Type type)
		{
			AddRequired(gameObject, type);
			return gameObject.AddComponent(type);
		}

		private static readonly Dictionary<Type, Type[]> requiredComponents = new Dictionary<Type, Type[]>();

		private static void AddRequired(GameObject gameObject, Type type)
		{
			foreach (Type required in RequiredComponents(type))
			{
				if (gameObject.GetComponent(required) == null)
				{
					AddRequired(gameObject, required);
					gameObject.AddComponent(required);
				}
			}
		}

		// The [RequireComponent] types of a script and its base classes that can be added (not abstract).
		private static Type[] RequiredComponents(Type type)
		{
			if (requiredComponents.TryGetValue(type, out Type[] cached))
			{
				return cached;
			}
			var found = new List<Type>();
			for (Type current = type; current != null && current != typeof(MonoBehaviour) && current != typeof(Component); current = current.BaseType)
			{
				foreach (RequireComponent attribute in current.GetCustomAttributes(typeof(RequireComponent), false))
				{
					foreach (Type required in new[] { attribute.m_Type0, attribute.m_Type1, attribute.m_Type2 })
					{
						if (required != null && !required.IsAbstract && !required.IsInterface && typeof(Component).IsAssignableFrom(required) && !found.Contains(required))
						{
							found.Add(required);
						}
					}
				}
			}
			cached = found.ToArray();
			requiredComponents[type] = cached;
			return cached;
		}

		// The game's factory templates are cloned as root objects (CloneTemplate.Instantiate); each canvas in the clone
		// gets the template's layout back once it is nested (see NestedCanvasRect).
		public static GameObject CloneTemplate_Instantiate(GameObject original, Vector3 position, Quaternion rotation)
		{
			GameObject clone = UnityEngine.Object.Instantiate(original, position, rotation);
			Canvas[] templates = original.GetComponentsInChildren<Canvas>(true);
			Canvas[] canvases = clone.GetComponentsInChildren<Canvas>(true);
			for (int i = 0; i < canvases.Length && i < templates.Length; i++)
			{
				if (canvases[i].GetComponent<NestedCanvasRect>() == null)
				{
					canvases[i].gameObject.AddComponent<NestedCanvasRect>().Capture((RectTransform)templates[i].transform);
				}
			}
			return clone;
		}

		// ScreenSignaler binds GeometryUtility's internal Internal_ExtractPlanes, which Unity 6 changed; its delegate goes
		// to the public CalculateFrustumPlanes, which fills the array the same way.
		public static Delegate ScreenSignaler_CreateDelegate(Type type, MethodInfo method)
		{
			return Delegate.CreateDelegate(type, typeof(Redirects).GetMethod(nameof(ExtractPlanes), BindingFlags.Static | BindingFlags.NonPublic));
		}

		private static void ExtractPlanes(Plane[] planes, Matrix4x4 matrix)
		{
			GeometryUtility.CalculateFrustumPlanes(matrix, planes);
		}

		// Brix's UnityLogAppender binds DebugLogHandler's internal Internal_Log, whose signature Unity 6 changed.
		public static Delegate UnityLogAppender_CreateDelegate(Type type, MethodInfo method)
		{
			return new Action<LogType, string, UnityEngine.Object>(BrixLog);
		}

		private static void BrixLog(LogType type, string message, UnityEngine.Object context)
		{
			Debug.LogFormat(type, LogOption.NoStacktrace, context, "{0}", message);
		}

		// LuaProperty makes open delegates on property accessors. On a struct's instance method (Color, Vector3...) that
		// needs the struct by ref, which Mono 2.6 did not check and current Mono refuses; those are compiled instead.
		public static Delegate LuaProperty_CreateDelegate(Type type, object firstArgument, MethodInfo method)
		{
			if (firstArgument != null || method.IsStatic || !method.DeclaringType.IsValueType)
			{
				return Delegate.CreateDelegate(type, firstArgument, method);
			}
			ParameterExpression[] parameters = type.GetMethod("Invoke").GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
			Expression call = Expression.Call(parameters[0], method, parameters.Skip(1));
			return Expression.Lambda(type, call, parameters).Compile();
		}

		// LuaDriverHQ scans UnityEngine.dll for component types. In Unity 6 that assembly only forwards to the engine
		// modules (Transform is in UnityEngine.CoreModule, Rigidbody in UnityEngine.PhysicsModule...), so for it the
		// modules are scanned. Types that do not load are left out, as the game's later builds did.
		public static Type[] LuaDriverHQ_GetTypes(Assembly assembly)
		{
			if (assembly.GetName().Name != "UnityEngine")
			{
				return LoadableTypes(assembly);
			}
			var types = new List<Type>();
			foreach (Assembly module in AppDomain.CurrentDomain.GetAssemblies())
			{
				string name = module.GetName().Name;
				if (name.StartsWith("UnityEngine.", StringComparison.Ordinal) && name.EndsWith("Module", StringComparison.Ordinal))
				{
					types.AddRange(LoadableTypes(module));
				}
			}
			return types.ToArray();
		}

		private static Type[] LoadableTypes(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				return e.Types.Where(t => t != null).ToArray();
			}
		}
	}
}
