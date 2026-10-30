using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The animators on objects of a name: controller, parameters, what each layer plays, and the local rotation of the
// object's children, to see whether an animation moves what it should (the sky's sun and moon orbits).
internal static class AnimatorEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/animators", "Animators on game objects whose name holds q: enabled, controller, parameters with their values, per layer the state playing (its clips and normalized time), and the local rotations of the object's children", Animators)
		{
			Parameters = "q (part of the game object's name, required), max (default 10), depth (children levels, default 3)"
		});
	}

	private static object Animators(ApiRequest request)
	{
		string q = request.Required("q");
		int max = request.Int("max", 10);
		int depth = request.Int("depth", 3);
		List<object> found = new List<object>();
		foreach (Animator animator in Object.FindObjectsOfType<Animator>())
		{
			if (found.Count >= max || !animator.name.Contains(q))
			{
				continue;
			}
			List<object> parameters = new List<object>();
			List<object> layers = new List<object>();
			if (animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
			{
				foreach (AnimatorControllerParameter parameter in animator.parameters)
				{
					object value = parameter.type == AnimatorControllerParameterType.Float ? Round(animator.GetFloat(parameter.nameHash))
						: parameter.type == AnimatorControllerParameterType.Int ? animator.GetInteger(parameter.nameHash)
						: (object)animator.GetBool(parameter.nameHash);
					parameters.Add(Obj("name", parameter.name, "type", parameter.type.ToString(), "value", value));
				}
				for (int layer = 0; layer < animator.layerCount; layer++)
				{
					AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
					List<object> clips = new List<object>();
					foreach (AnimatorClipInfo clip in animator.GetCurrentAnimatorClipInfo(layer))
					{
						clips.Add(Obj("clip", clip.clip != null ? clip.clip.name : null, "weight", Round(clip.weight), "length", clip.clip != null ? (object)Round(clip.clip.length) : null));
					}
					layers.Add(Obj("layer", animator.GetLayerName(layer), "weight", Round(animator.GetLayerWeight(layer)), "stateHash", state.fullPathHash, "normalizedTime", Round(state.normalizedTime), "clips", clips));
				}
			}
			found.Add(Obj(
				"name", animator.name,
				"path", Path(animator.transform),
				"enabled", animator.isActiveAndEnabled,
				"controller", animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : null,
				"cullingMode", animator.cullingMode.ToString(),
				"updateMode", animator.updateMode.ToString(),
				"parameters", parameters,
				"layers", layers,
				"transforms", Transforms(animator.transform, depth)));
		}
		return Obj("count", found.Count, "animators", found);
	}

	private static object Transforms(Transform transform, int depth)
	{
		List<object> children = new List<object>();
		if (depth > 0)
		{
			foreach (Transform child in transform)
			{
				children.Add(Transforms(child, depth - 1));
			}
		}
		return Obj("name", transform.name, "localEuler", Vec(transform.localEulerAngles), "localPosition", Vec(transform.localPosition), "children", children);
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
