using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleStoryPlus.Player
{
	// Development: CSP_PLAYTEST=<map> starts a single-player sandbox on that map once the title screen has settled, the
	// way the new game screen does. Logs the game's state every 10 seconds and, with CSP_SHOT_DIR, takes screenshots 30,
	// 60 and 90 seconds after the level loaded. The game's assemblies are installed after this player is built, so it
	// reaches them by reflection.
	public sealed class PlayTest : MonoBehaviour
	{
		private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		private string map;
		private bool started;
		private float menuSince = -1f;
		private float levelSince = -1f;
		private int shots;

		public static void StartIfAsked()
		{
			string map = Environment.GetEnvironmentVariable("CSP_PLAYTEST");
			if (string.IsNullOrEmpty(map))
			{
				return;
			}
			var host = new GameObject("CSP PlayTest");
			DontDestroyOnLoad(host);
			host.AddComponent<PlayTest>().map = map;
		}

		private IEnumerator Start()
		{
			while (true)
			{
				Debug.Log("[CSP] playtest t=" + Time.realtimeSinceStartup.ToString("0") + " frame=" + Time.frameCount + " fps=" + (1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)).ToString("0") + " " + State());
				if (levelSince >= 0f)
				{
					Diagnostics.LogPostProcessing();
				}
				yield return new WaitForSecondsRealtime(10f);
			}
		}

		private void Update()
		{
			if (!started)
			{
				TryStartWorld();
			}
			TryScreenshot();
		}

		private static Type GameType(string name)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type type = assembly.GetType(name, false);
				if (type != null)
				{
					return type;
				}
			}
			throw new TypeLoadException(name);
		}

		private static object Get(Type type, string member)
		{
			return Member(type, null, member, Static);
		}

		private static object Get(object target, string member)
		{
			return Member(target.GetType(), target, member, Instance);
		}

		private static object Member(Type type, object target, string member, BindingFlags flags)
		{
			for (Type t = type; t != null; t = t.BaseType)
			{
				PropertyInfo property = t.GetProperty(member, flags | BindingFlags.DeclaredOnly);
				if (property != null)
				{
					return property.GetValue(target);
				}
				FieldInfo field = t.GetField(member, flags | BindingFlags.DeclaredOnly);
				if (field != null)
				{
					return field.GetValue(target);
				}
			}
			throw new MissingMemberException(type.FullName, member);
		}

		private static bool LevelLoaded()
		{
			Type architecte = GameType("Brix.Engine.Architecte");
			return Get(architecte, "instance") as UnityEngine.Object != null && (bool)Get(architecte, "commence");
		}

		// Where the world start stands: GameParam waiting for the loading screen's slide, the slides, UNet.
		private static string StartState()
		{
			var parts = new System.Collections.Generic.List<string>();
			object param = Get(GameType("GameParam"), "instance");
			parts.Add("waitingForAnim=" + Get(param, "_waitingForAnim"));
			Type slide = GameType("LoadingScreenSlide");
			FieldInfo slideState = slide.GetField("state", Instance);
			foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(slide))
			{
				var component = (Component)found;
				if (component.gameObject.scene.IsValid())
				{
					parts.Add("slide " + component.name + (component.gameObject.activeInHierarchy ? " active " : " inactive ") + slideState.GetValue(component));
				}
			}
			object manager = Get(GameType("Brix.Network.UNetManager"), "instance");
			if (manager as UnityEngine.Object != null)
			{
				parts.Add("networkActive=" + Get(manager, "isNetworkActive"));
				parts.Add("server=" + Get(GameType("UnityEngine.Networking.NetworkServer"), "active"));
				parts.Add("client=" + Get(GameType("UnityEngine.Networking.NetworkClient"), "active"));
				parts.Add("networkScene=" + Get(GameType("UnityEngine.Networking.NetworkManager"), "networkSceneName"));
			}
			else
			{
				parts.Add("no UNetManager");
			}
			return string.Join(" ", parts);
		}

		private string State()
		{
			string scenes = string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));
			try
			{
				Type architecte = GameType("Brix.Engine.Architecte");
				bool game = Get(architecte, "instance") as UnityEngine.Object != null;
				return (game ? "game commence=" + Get(architecte, "commence") + " termine=" + Get(architecte, "termine") : "no game") + " scenes: " + scenes + (started ? " | " + StartState() : "");
			}
			catch (Exception e)
			{
				return "state failed: " + e.Message + " scenes: " + scenes;
			}
		}

		private void TryStartWorld()
		{
			bool onMenu = SceneManager.GetActiveScene().name == "SceneMenu"
				&& Get(GameType("GameParam"), "instance") as UnityEngine.Object != null
				&& Get(GameType("Brix.Engine.Architecte"), "instance") as UnityEngine.Object == null;
			if (!onMenu)
			{
				menuSince = -1f;
				return;
			}
			if (menuSince < 0f)
			{
				menuSince = Time.realtimeSinceStartup;
			}
			if (Time.realtimeSinceStartup - menuSince < 5f)
			{
				return;
			}
			started = true;
			try
			{
				Type queryParams = GameType("Brix.Assets.AssetQueryParameters");
				Type playable = GameType("Brix.Assets.AssetPlayableContext");
				Type menu = GameType("Brix.UI.Builder.Menu.BaseMapLibraryMenu");
				object singleplayer = Enum.Parse(playable, "Singleplayer");
				object spSources = Enum.Parse(queryParams, "AllSPSources");
				object handler = Get(GameType("Brix.Assets.AssetDispensary"), "maps");
				MethodInfo query = handler.GetType().GetMethods(Instance).First(m => m.Name == "SendQuery" && m.GetParameters().Length == 3);
				var maps = (IEnumerable)query.Invoke(handler, new[] { Enum.Parse(queryParams, "AllSPSources, RefreshCache"), singleplayer, (object)true });
				object found = null;
				foreach (object candidate in maps)
				{
					string shortName = (string)candidate.GetType().GetProperty("shortCodename").GetValue(candidate);
					string display = (string)candidate.GetType().GetProperty("displayName").GetValue(candidate);
					if (string.Equals(shortName, map, StringComparison.OrdinalIgnoreCase) || string.Equals(display, map, StringComparison.OrdinalIgnoreCase))
					{
						found = candidate;
						break;
					}
				}
				if (found == null)
				{
					Debug.LogError("[CSP] playtest: no map " + map);
					return;
				}
				var modes = (IEnumerable)found.GetType().GetMethod("GetGamemodes").Invoke(found, new[] { singleplayer });
				object mode = modes.Cast<object>().First(m => m != null && (string)m.GetType().GetProperty("codename").GetValue(m) == "sandbox");
				object preset = mode.GetType().GetMethod("GetDefaultPreset").Invoke(mode, new[] { spSources, singleplayer });
				Type assetMenu = GameType("Brix.UI.Builder.Menu.BaseAssetMenu");
				menu.GetMethods(Static).First(m => m.Name == "SetSelectedMap" && m.GetParameters().Length == 4)
					.Invoke(null, new[] { found, singleplayer, Enum.Parse(assetMenu.GetNestedType("MenuGameContext"), "Normal"), Enum.Parse(assetMenu.GetNestedType("MenuTypeContext"), "New") });
				menu.GetMethods(Static).First(m => m.Name == "SetSelectedGamemode" && m.GetParameters().Length == 1).Invoke(null, new[] { mode });
				menu.GetMethod("SetSelectedPreset", Static).Invoke(null, new[] { preset });
				menu.GetMethod("SetSelectedAsLoaded", Static).Invoke(null, new object[] { false });
				Action emptySelected = () => menu.GetMethod("EmptySelected", Static).Invoke(null, null);
				GameType("GameParam").GetMethod("StartGame", Static).Invoke(null, new object[] { emptySelected });
				Debug.Log("[CSP] playtest: starting " + map + " (sandbox)");
			}
			catch (Exception e)
			{
				Debug.LogError("[CSP] playtest: world start failed: " + e);
			}
		}

		// Closes the game mode's introduction ("I'M READY!"), so the screenshots show the world.
		private static IEnumerator PressReady()
		{
			for (int attempt = 0; attempt < 30; attempt++)
			{
				yield return new WaitForSecondsRealtime(2f);
				if (TryPressReady())
				{
					yield break;
				}
			}
			Debug.Log("[CSP] playtest: no ready button; texts: " + string.Join(" | ", FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
				.Select(b => (b, p: b.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)))
				.Where(x => x.p != null && x.p.PropertyType == typeof(string))
				.Select(x => x.b.GetType().Name + ":" + x.p.GetValue(x.b))
				.Take(60)));
		}

		// Keeps the main camera where the level started it: the mouse is outside the window during a test, and the game
		// scrolls the view at the screen's edge.
		private static void PinCamera()
		{
			Camera main = Camera.main;
			if (main == null)
			{
				return;
			}
			Vector3 position = main.transform.position;
			Quaternion rotation = main.transform.rotation;
			// CSP_PLAYTEST_FORWARD=1: the main camera renders forward instead of deferred.
			if (Environment.GetEnvironmentVariable("CSP_PLAYTEST_FORWARD") == "1")
			{
				main.renderingPath = RenderingPath.Forward;
			}
			// CSP_PLAYTEST_VIEW=horizon: low over the ground the start view looks at, toward the horizon.
			if (Environment.GetEnvironmentVariable("CSP_PLAYTEST_VIEW") == "horizon")
			{
				Vector3 ground = Physics.Raycast(position, main.transform.forward, out RaycastHit hit, 1000f) ? hit.point : position - new Vector3(0f, 120f, 0f);
				position = ground + new Vector3(0f, 25f, -60f);
				rotation = Quaternion.LookRotation(Vector3.forward);
			}
			Camera.onPreCull += camera =>
			{
				if (camera == main && !shooting)
				{
					camera.transform.SetPositionAndRotation(position, rotation);
				}
			};
		}

		private static bool shooting;

		// The introduction's button runs the Lua hook UIGame.hk_CloseModalMenu (UIGameObserver.lua_CloseModalMenu); the
		// game's menus take input of their own, so the hook is invoked directly once the button is there.
		private static bool TryPressReady()
		{
			bool shown = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(b =>
			{
				PropertyInfo property = b.isActiveAndEnabled ? b.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public) : null;
				return property != null && property.PropertyType == typeof(string) && property.GetValue(b) is string label && label.IndexOf("READY", StringComparison.OrdinalIgnoreCase) >= 0;
			});
			if (!shown)
			{
				return false;
			}
			object hook = Get(GameType("UIGameObserver"), "lua_CloseModalMenu");
			MethodInfo invoke = GameType("Brix.Lua.LuaHookBase").GetMethods(Static).First(m => m.Name == "InvokeFromCLR" && m.GetParameters().Length == 1);
			invoke.Invoke(null, new[] { hook });
			Debug.Log("[CSP] playtest: closed the introduction");
			return true;
		}

		// The screen with the main camera's image effects all off, then turned on again one at a time.
		private static IEnumerator EffectsTest(string dir)
		{
			Camera camera = Camera.main;
			// Everything on the camera that can draw (image effects, command buffer effects), not the game's own logic.
			string[] logic = { "LuaDriver", "SerializationComponent", "FactorylessImprint", "AkAudioListener", "CameraScript", "SkyboxCameraDrawer", "PlayTest" };
			Behaviour[] effects = camera.GetComponents<Behaviour>().Where(b => b.enabled && !(b is Camera) && !logic.Contains(b.GetType().Name)).ToArray();
			foreach (Behaviour effect in effects)
			{
				effect.enabled = false;
			}
			yield return new WaitForSecondsRealtime(1f);
			ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "effects_0_none.png"));
			for (int i = 0; i < effects.Length; i++)
			{
				effects[i].enabled = true;
				yield return new WaitForSecondsRealtime(1f);
				string file = System.IO.Path.Combine(dir, "effects_" + (i + 1) + "_" + effects[i].GetType().Name + ".png");
				ScreenCapture.CaptureScreenshot(file);
				Debug.Log("[CSP] playtest effects " + file);
			}
		}

		// The renderers the main camera's ray through a screen point passes, nearest first (bottom-left origin, 1600x900).
		private static void ProbePixel(Vector2 point)
		{
			Vector2 screen = new Vector2(point.x * Screen.width / 1600f, point.y * Screen.height / 900f);
			Debug.Log("[CSP] playtest probe " + point + " ui: " + string.Join(" | ", FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None)
				.Where(g => g.isActiveAndEnabled && g.canvas != null && RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, screen, g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera))
				.Select(g => g.name + " (" + g.GetType().Name + " size " + g.rectTransform.rect.size + " scale " + g.rectTransform.lossyScale + " canvas " + g.canvas.name + " " + g.canvas.renderMode + " scaleFactor " + g.canvas.scaleFactor + " sprite " + ((g as UnityEngine.UI.Image)?.sprite != null ? ((UnityEngine.UI.Image)g).sprite.name + " " + ((UnityEngine.UI.Image)g).sprite.rect.size : "none") + " type " + (g as UnityEngine.UI.Image)?.type + " parent " + (g.transform.parent != null ? g.transform.parent.name : "-") + ")")
				.Take(3)));
			foreach (Camera camera in Camera.allCameras)
			{
				Ray ray = camera.ScreenPointToRay(new Vector3(point.x * camera.pixelWidth / 1600f, point.y * camera.pixelHeight / 900f, 0f));
				var hits = new System.Collections.Generic.List<(float, Renderer)>();
				foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
				{
					if (renderer.enabled && renderer.gameObject.activeInHierarchy && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 && renderer.bounds.IntersectRay(ray, out float distance))
					{
						hits.Add((distance, renderer));
					}
				}
				Debug.Log("[CSP] playtest probe " + point + " camera depth " + camera.depth + ": " + string.Join(" | ", hits.OrderBy(h => h.Item1).Take(8).Select(h => h.Item2.name + " (" + h.Item2.GetType().Name + ", layer " + h.Item2.gameObject.layer + ", " + h.Item1.ToString("0") + " m, " + (h.Item2.sharedMaterial != null ? h.Item2.sharedMaterial.name + " / " + h.Item2.sharedMaterial.shader.name : "no material") + ")")));
			}
		}

		// The main camera alone (no UI), with where it is.
		private static void CameraShot(string file, RenderingPath? path, (Vector3 From, Vector3 At)? view)
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				Debug.Log("[CSP] playtest: no main camera");
				return;
			}
			RenderingPath previousPath = camera.renderingPath;
			Vector3 previousPosition = camera.transform.position;
			Quaternion previousRotation = camera.transform.rotation;
			shooting = true;
			if (view.HasValue)
			{
				camera.transform.position = view.Value.From;
				camera.transform.LookAt(view.Value.At);
			}
			if (path.HasValue)
			{
				camera.renderingPath = path.Value;
			}
			var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
			// No sky: it is another camera's (the skybox scene, which SkyboxCameraDrawer moves with the main camera), and
			// the main camera only clears depth. The sky is whatever the new texture holds: black on Direct3D 11.
			RenderTexture previous = camera.targetTexture;
			camera.targetTexture = target;
			camera.Render();
			camera.targetTexture = previous;
			camera.renderingPath = previousPath;
			RenderTexture.active = target;
			var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
			image.Apply();
			RenderTexture.active = null;
			System.IO.File.WriteAllBytes(file, image.EncodeToPNG());
			Destroy(image);
			target.Release();
			Vector3 shotPosition = camera.transform.position;
			Vector3 shotForward = camera.transform.forward;
			camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
			shooting = false;
			Debug.Log("[CSP] playtest camera " + file + " at " + shotPosition + " looking " + shotForward + " far " + camera.farClipPlane + " path " + camera.actualRenderingPath + " visible renderers " + FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.isVisible));
		}

		private void TryScreenshot()
		{
			string dir = Environment.GetEnvironmentVariable("CSP_SHOT_DIR");
			if (string.IsNullOrEmpty(dir) || shots >= 3 || !started || !LevelLoaded())
			{
				return;
			}
			if (levelSince < 0f)
			{
				levelSince = Time.realtimeSinceStartup;
				Debug.Log("[CSP] playtest: level loaded");
				StartCoroutine(PressReady());
				PinCamera();
				Diagnostics.LogShaders();
				Diagnostics.LogCameras();
				Diagnostics.LogUiMaterials();
			}
			if (Time.realtimeSinceStartup - levelSince < 30f * (shots + 1))
			{
				return;
			}
			shots++;
			string file = System.IO.Path.Combine(dir, "play_" + shots + ".png");
			ScreenCapture.CaptureScreenshot(file);
			Debug.Log("[CSP] playtest screenshot " + file);
			CameraShot(System.IO.Path.Combine(dir, "camera_" + shots + ".png"), null, null);
			// Fixed views from the ground the camera looks at (the level start's view, see PinCamera).
			Transform view = Camera.main.transform;
			Vector3 centre = Physics.Raycast(view.position, view.forward, out RaycastHit hit, 1000f) ? hit.point : view.position - new Vector3(0f, 120f, 0f);
			CameraShot(System.IO.Path.Combine(dir, "above_" + shots + ".png"), null, (centre + new Vector3(-40f, 50f, -40f), centre));
			CameraShot(System.IO.Path.Combine(dir, "horizon_" + shots + ".png"), null, (centre + new Vector3(0f, 25f, -60f), centre + new Vector3(0f, 25f, 0f)));
			CameraShot(System.IO.Path.Combine(dir, "forward_" + shots + ".png"), RenderingPath.Forward, (centre + new Vector3(-40f, 50f, -40f), centre));
			Debug.Log("[CSP] playtest visible: " + string.Join(" | ", FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.isVisible && (Camera.main.cullingMask & (1 << r.gameObject.layer)) != 0)
				.GroupBy(r => r.gameObject.layer + " " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none"))
				.Select(g => g.Key + " x" + g.Count() + " e.g. " + g.First().name + " " + g.First().bounds.center.ToString("0") + "+" + g.First().bounds.extents.ToString("0"))));
			foreach (Canvas template in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c.name == "SmallArrow").Take(6))
			{
				var r = (RectTransform)template.transform;
				Debug.Log("[CSP] playtest arrow " + template.gameObject.scene.name + " active=" + template.gameObject.activeInHierarchy + " root=" + template.isRootCanvas + " anchors " + r.anchorMin + "-" + r.anchorMax + " size " + r.rect.size + " top " + template.transform.root.name);
			}
			UnityEngine.UI.Graphic arrow = FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None).FirstOrDefault(g => g.name == "SmallArrow" && g.isActiveAndEnabled);
			if (arrow != null)
			{
				var chain = new System.Collections.Generic.List<string>();
				for (Transform t = arrow.transform; t != null; t = t.parent)
				{
					Canvas c = t.GetComponent<Canvas>();
					RectTransform r = t as RectTransform;
					chain.Add(t.name + (c != null ? " [Canvas " + (c.enabled ? "on" : "off") + " root=" + c.isRootCanvas + " override=" + c.overrideSorting + " mode=" + c.renderMode + "]" : "") + (r != null ? " size " + r.rect.size + " anchors " + r.anchorMin + "-" + r.anchorMax : " (Transform)") + (t.gameObject.activeSelf ? "" : " inactive"));
				}
				Debug.Log("[CSP] playtest arrow chain: " + string.Join(" <- ", chain));
			}
			ProbePixel(new Vector2(800f, 900f - 250f));
			ProbePixel(new Vector2(500f, 900f - 400f));
			if (shots == 1 && Environment.GetEnvironmentVariable("CSP_EFFECTS_TEST") == "1")
			{
				StartCoroutine(EffectsTest(dir));
			}
		}
	}
}
