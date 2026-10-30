using System.Collections.Generic;
using Brix.External.Factories;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// A factory asset's components as its template has them, and optionally as a plain clone of it has them: for finding
// components that are lost on the way (a missing script, a component that does not survive Instantiate).
internal static class TemplateEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/template", "A factory asset's template: its components in order (type, enabled, missing script) and its children's; with clone=true also those of a plain Instantiate of it, destroyed again at once", Template)
		{
			Parameters = "key (Factory/Name, e.g. Blocks/Stone_DoubleBrick; required), clone (true: also a clone's), depth (children levels, default 1)"
		});
	}

	private static object Template(ApiRequest request)
	{
		string[] parts = request.Required("key").Split('/');
		if (parts.Length != 2)
		{
			throw new ApiException(400, "key is Factory/Name");
		}
		Factory.AssetKey key = new Factory.AssetKey(parts[0], parts[1]);
		if (!Factory.Exist(key))
		{
			throw new ApiException(404, "no asset " + parts[0] + "/" + parts[1]);
		}
		GameObject template = Factory.Peek(key);
		int depth = request.Int("depth", 1);
		object clone = null;
		if (request.Bool("clone", false))
		{
			GameObject copy = Object.Instantiate(template);
			clone = Describe(copy, depth);
			Object.DestroyImmediate(copy);
		}
		return Obj("key", parts[0] + "/" + parts[1], "template", Describe(template, depth), "clone", clone);
	}

	private static object Describe(GameObject go, int depth)
	{
		List<object> components = new List<object>();
		foreach (Component component in go.GetComponents<Component>())
		{
			if (component == null)
			{
				components.Add(Obj("type", null, "missing", true));
				continue;
			}
			Behaviour behaviour = component as Behaviour;
			components.Add(Obj("type", component.GetType().FullName, "enabled", behaviour != null ? (object)behaviour.enabled : null));
		}
		List<object> children = new List<object>();
		if (depth > 0)
		{
			foreach (Transform child in go.transform)
			{
				children.Add(Describe(child.gameObject, depth - 1));
			}
		}
		return Obj("name", go.name, "active", go.activeSelf, "components", components, "children", children);
	}
}
