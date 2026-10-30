using System;
using System.Linq;
using CastleStoryPlus.Compat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleStoryPlus.Player
{
	// Startup diagnostics for the port (CSP_DIAG=1): loaded assemblies, the game's MonoScripts, scene contents, and
	// screenshots at 20, 35 and 50 s into CSP_SHOT_DIR.
	internal static class Diagnostics
	{
		public static void StartScreenshots()
		{
			string dir = Environment.GetEnvironmentVariable("CSP_SHOT_DIR");
			if (string.IsNullOrEmpty(dir))
			{
				return;
			}
			var host = new GameObject("CSP Screenshots");
			UnityEngine.Object.DontDestroyOnLoad(host);
			host.AddComponent<Screenshots>().Directory = dir;
		}

		private sealed class Screenshots : MonoBehaviour
		{
			public string Directory;
			private readonly float[] times = { 20f, 35f, 50f };
			private int next;

			private void Update()
			{
				if (next < times.Length && Time.realtimeSinceStartup >= times[next])
				{
					string file = System.IO.Path.Combine(Directory, "shot_" + times[next].ToString("0") + ".png");
					ScreenCapture.CaptureScreenshot(file);
					Debug.Log("[CSP] screenshot " + file);
					next++;
				}
			}
		}

		// Active UI graphics drawn with a material of their own (not UI/Default), with where they are on screen.
		public static void LogUiMaterials()
		{
			var corners = new Vector3[4];
			foreach (UnityEngine.UI.Graphic graphic in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
			{
				Material material = graphic.material;
				if (!graphic.isActiveAndEnabled || material == null || material == graphic.defaultMaterial)
				{
					continue;
				}
				graphic.rectTransform.GetWorldCorners(corners);
				Debug.Log("[CSP]   ui " + graphic.name + " (" + graphic.GetType().Name + ") material " + material.name + " shader " + material.shader.name + " passes " + material.shader.passCount + " corners " + corners[0] + " " + corners[2]);
			}
		}

		// Every camera in the order they draw, with what it draws and the components on it (image effects).
		public static void LogCameras()
		{
			foreach (Camera camera in Camera.allCameras.OrderBy(c => c.depth))
			{
				string components = string.Join(", ", camera.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is Camera)).Select(c => c.GetType().Name + ((c is Behaviour b && !b.enabled) ? " (off)" : "")));
				Debug.Log("[CSP]   camera " + camera.name + " depth=" + camera.depth + " clear=" + camera.clearFlags + " mask=0x" + camera.cullingMask.ToString("x") + " target=" + (camera.targetTexture != null ? camera.targetTexture.name : "screen") + " hdr=" + camera.allowHDR + " | " + components);
			}
		}

		// The main camera's post-processing (the game's PostProcessingBehaviour, by reflection): the profile's effects that
		// are on, whether the eye adaptation's compute shader loads with its kernel, and the exposure it last worked out.
		public static void LogPostProcessing()
		{
			Camera camera = Camera.main;
			Component behaviour = camera != null ? camera.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "PostProcessingBehaviour") : null;
			if (behaviour == null)
			{
				Debug.Log("[CSP] post-processing: none on the main camera");
				return;
			}
			const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
			object profile = behaviour.GetType().GetField("profile", fields)?.GetValue(behaviour);
			string on = profile == null ? "no profile" : string.Join(", ", profile.GetType().GetFields(fields)
				.Select(f => (f.Name, Model: f.GetValue(profile)))
				.Where(m => m.Model != null && m.Model.GetType().GetProperty("enabled")?.GetValue(m.Model) is bool enabled && enabled)
				.Select(m => m.Name));
			var compute = Resources.Load<ComputeShader>("Shaders/EyeHistogram");
			string kernel = compute == null ? "not loaded" : compute.HasKernel("KEyeHistogram") ? "kernel found" : "no kernel";
			string exposure = "-";
			object eye = behaviour.GetType().GetField("m_EyeAdaptation", fields)?.GetValue(behaviour);
			if (eye?.GetType().GetField("m_CurrentAutoExposure", fields)?.GetValue(eye) is RenderTexture current && current.IsCreated())
			{
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = current;
				var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false);
				pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
				RenderTexture.active = previous;
				exposure = pixel.GetPixel(0, 0).r.ToString("0.#####");
				UnityEngine.Object.Destroy(pixel);
			}
			Debug.Log("[CSP] post-processing: on: " + on + " | compute supported=" + SystemInfo.supportsComputeShaders + ", EyeHistogram " + kernel + " | auto exposure " + exposure);
		}

		// Every loaded shader: whether this GPU and Unity 6 can run it, and how many renderers use it.
		public static void LogShaders()
		{
			var users = new System.Collections.Generic.Dictionary<Shader, int>();
			foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material != null && material.shader != null)
					{
						users.TryGetValue(material.shader, out int count);
						users[material.shader] = count + 1;
					}
				}
			}
			Shader[] shaders = Resources.FindObjectsOfTypeAll<Shader>();
			Debug.Log("[CSP] " + shaders.Length + " shaders, " + shaders.Count(s => s.isSupported) + " supported");
			foreach (Shader shader in shaders.OrderByDescending(s => users.TryGetValue(s, out int n) ? n : 0).Take(40))
			{
				users.TryGetValue(shader, out int used);
				Debug.Log("[CSP]   shader " + shader.name + " supported=" + shader.isSupported + " passes=" + shader.passCount + " renderers=" + used);
			}
		}

		public static void LogAssemblies()
		{
			string[] names = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).OrderBy(n => n).ToArray();
			Debug.Log("[CSP] " + names.Length + " assemblies: " + string.Join(", ", names.Where(n => !n.StartsWith("UnityEngine.", StringComparison.Ordinal) && !n.StartsWith("System", StringComparison.Ordinal))));
		}

		public static void LogScripts()
		{
			foreach (AssetBundle bundle in GameContent.Bundles)
			{
				string[] scripts = bundle.GetAllAssetNames().Where(n => n.StartsWith("monoscript/", StringComparison.Ordinal)).Take(3).ToArray();
				foreach (string name in scripts)
				{
					UnityEngine.Object script = bundle.LoadAsset(name);
					Debug.Log("[CSP] " + name + ": " + (script == null ? "null" : script.GetType().FullName + " '" + script.name + "'"));
				}
			}
		}

		public static void LogPrefab(string path)
		{
			GameObject prefab = Resources.Load<GameObject>(path);
			if (prefab == null)
			{
				Debug.Log("[CSP] prefab " + path + ": not found");
				return;
			}
			string[] components = prefab.GetComponentsInChildren<Component>(true).Select(c => c == null ? "MISSING" : c.GetType().FullName).ToArray();
			Debug.Log("[CSP] prefab " + path + ": " + string.Join(", ", components));
		}

		public static void LogScene(Scene scene)
		{
			int missing = 0;
			int total = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Component component in root.GetComponentsInChildren<Component>(true))
				{
					total++;
					if (component == null)
					{
						missing++;
					}
				}
			}
			Debug.Log("[CSP] scene " + scene.name + ": " + scene.rootCount + " roots, " + total + " components, " + missing + " missing");
		}
	}
}
