using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using static CastleStoryPlus.DevTools.Endpoints.Json;
using Object = UnityEngine.Object;

namespace CastleStoryPlus.DevTools.Endpoints;

// Every live component of a type, by its type's name: where it sits (path, scene, active, enabled), to find
// duplicates (two EventSystems, two audio listeners) and leftovers; with fields=true also the values of their serialized
// fields and their local transform, to compare what the game's assets hold on 5.6 and in the Unity 6 port.
internal static class ComponentEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/components", "Live components whose type's name is q (or ends with .q): path, scene, active, enabled and the other components on the object; also inactive ones", Get)
		{
			Parameters = "q (type name, e.g. EventSystem or UnityEngine.EventSystems.EventSystem; required), max (default 50), path (only objects whose path holds this text), fields (true: the serialized fields' values and the local position, rotation and scale)"
		});
	}

	private static object Get(ApiRequest request)
	{
		string q = request.Required("q");
		int max = request.Int("max", 50);
		string path = request.String("path", null);
		bool fields = request.Bool("fields", false);
		List<object> found = new List<object>();
		int total = 0;
		foreach (Component component in Resources.FindObjectsOfTypeAll<Component>())
		{
			if (component == null || component.gameObject.scene.name == null || !Matches(component.GetType(), q))
			{
				continue;
			}
			if (path != null && Path(component.transform).IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			total++;
			if (found.Count >= max)
			{
				continue;
			}
			Behaviour behaviour = component as Behaviour;
			List<string> others = new List<string>();
			foreach (Component other in component.GetComponents<Component>())
			{
				if (other != null && other != component && !(other is Transform))
				{
					others.Add(other.GetType().Name);
				}
			}
			Scene scene = component.gameObject.scene;
			found.Add(Obj(
				"path", Path(component.transform),
				"type", component.GetType().FullName,
				"scene", scene.IsValid() ? scene.name : null,
				"active", component.gameObject.activeInHierarchy,
				"enabled", behaviour != null ? (object)behaviour.enabled : null,
				"others", others,
				"transform", fields ? LocalTransform(component.transform) : null,
				"fields", fields ? Fields(component) : null));
		}
		return Obj("q", q, "count", total, "components", found);
	}

	private static object LocalTransform(Transform transform)
	{
		return Obj(
			"position", Text(transform.localPosition),
			"rotation", Text(transform.localEulerAngles),
			"scale", Text(transform.localScale));
	}

	// The fields Unity serializes (public, or marked SerializeField, not NonSerialized), of the type and its bases
	// up to MonoBehaviour, as text.
	private static object Fields(Component component)
	{
		Dictionary<string, object> values = new Dictionary<string, object>();
		for (Type t = component.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
		{
			foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				bool serialized = (field.IsPublic || field.IsDefined(typeof(SerializeField), true)) && !field.IsNotSerialized;
				if (!serialized || values.ContainsKey(field.Name))
				{
					continue;
				}
				object value;
				try
				{
					value = Text(field.GetValue(component));
				}
				catch (Exception e)
				{
					value = "(" + e.GetType().Name + ")";
				}
				values[field.Name] = value;
			}
		}
		return values;
	}

	private static string Text(object value)
	{
		if (value == null)
		{
			return null;
		}
		if (value is Object unityObject)
		{
			return unityObject != null ? unityObject.name + " (" + unityObject.GetType().Name + ")" : "null";
		}
		if (value is Matrix4x4 matrix)
		{
			return Text(matrix.GetRow(0)) + " | " + Text(matrix.GetRow(1)) + " | " + Text(matrix.GetRow(2)) + " | " + Text(matrix.GetRow(3));
		}
		if (value is Vector4 v4)
		{
			return F(v4.x) + " " + F(v4.y) + " " + F(v4.z) + " " + F(v4.w);
		}
		if (value is Vector3 v3)
		{
			return F(v3.x) + " " + F(v3.y) + " " + F(v3.z);
		}
		if (value is IFormattable formattable)
		{
			return formattable.ToString(null, CultureInfo.InvariantCulture);
		}
		return value.ToString();
	}

	private static string F(float value)
	{
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private static bool Matches(Type type, string q)
	{
		for (Type t = type; t != null && t != typeof(Component); t = t.BaseType)
		{
			if (t.Name == q || t.FullName == q)
			{
				return true;
			}
		}
		return false;
	}

	private static string Path(Transform transform)
	{
		string path = transform.name;
		for (Transform parent = transform.parent; parent != null; parent = parent.parent)
		{
			path = parent.name + "/" + path;
		}
		return path;
	}
}
