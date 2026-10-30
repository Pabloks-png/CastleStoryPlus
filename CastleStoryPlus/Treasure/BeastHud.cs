using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using Brix.NewUI.Compass;
using CastleStoryPlus.Core;
using CastleStoryPlus.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.Treasure;

// What the player sees of the beasts besides their bodies:
// - their own icon (Assets/Beasts/<name>_icon.png, made by tools/beasts/icons.py) on the markers at the screen's edge
//   that point to enemies out of view, instead of the corruptron's or biftron's occupation icon;
// - a health bar over each beast that is hurt or fighting (their own unit's meshes, which the game's look of damage
//   is on, are hidden under the beast's body).
[Feature(Features.Treasure, Features.TreasureInfo)]
[HarmonyPatch(typeof(CompassDispenser), nameof(CompassDispenser.GetOrCreateSoloCompass))]
internal class BeastHud : MonoBehaviour
{
	private const float BarWidth = 46f;

	private const float BarHeight = 6f;

	private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

	private sealed class Bar
	{
		public RectTransform Rect;

		public RectTransform Fill;
	}

	private readonly List<Bar> _bars = new List<Bar>();

	private RectTransform _canvas;

	private static void Enable()
	{
		Plugin.Root.AddComponent<BeastHud>();
	}

	private static void Prefix(GameObject go, ref Sprite icon)
	{
		BeastInfo info = Beasts.KindOf(go);
		if (info != null)
		{
			icon = Icon(info) ?? icon;
		}
	}

	private static Sprite Icon(BeastInfo info)
	{
		if (Icons.TryGetValue(info.Name, out Sprite sprite))
		{
			return sprite;
		}
		string file = Path.Combine(Path.Combine(Path.Combine(Plugin.Directory, "Assets"), "Beasts"), info.Name + "_icon.png");
		if (File.Exists(file))
		{
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = info.Name + " icon", wrapMode = TextureWrapMode.Clamp };
			texture.LoadImage(File.ReadAllBytes(file));
			sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
			sprite.name = info.Name;
		}
		else
		{
			Plugin.Log.LogWarning("Treasure: no icon " + file);
		}
		Icons[info.Name] = sprite;
		return sprite;
	}

	private void LateUpdate()
	{
		Camera camera = Camera.main;
		int shown = 0;
		if (camera != null)
		{
			foreach (Labor labor in Beasts.All)
			{
				if (!Beasts.IsAliveBeast(labor))
				{
					continue;
				}
				DamageReceiver health = labor.GetComponent<BricktronDamageReceiver>();
				if (health == null || health.MaxHP <= 0f)
				{
					continue;
				}
				float part = Mathf.Clamp01(health.CurrentHP / health.MaxHP);
				if (part >= 0.999f && BeastTemper.IsCalm(labor))
				{
					continue;
				}
				Vector3 screen = camera.WorldToScreenPoint(labor.transform.position + Vector3.up * Height(Beasts.KindOf(labor)));
				if (screen.z <= 0f)
				{
					continue;
				}
				Bar bar = GetBar(shown++);
				bar.Rect.position = new Vector3(screen.x, screen.y, 0f);
				bar.Fill.anchorMax = new Vector2(part, 1f);
				if (!bar.Rect.gameObject.activeSelf)
				{
					bar.Rect.gameObject.SetActive(true);
				}
			}
		}
		for (int i = shown; i < _bars.Count; i++)
		{
			if (_bars[i].Rect.gameObject.activeSelf)
			{
				_bars[i].Rect.gameObject.SetActive(false);
			}
		}
	}

	// Over the beast's head (in blocks above its feet).
	private static float Height(BeastInfo info)
	{
		return (info != null && info.Kind == BeastKind.Spirestalker) ? 4.6f : 3f;
	}

	private Bar GetBar(int index)
	{
		if (_canvas == null)
		{
			_canvas = UiKit.CreateCanvas("CastleStoryPlusBeastBars", transform, 5).GetComponent<RectTransform>();
			_canvas.GetComponent<GraphicRaycaster>().enabled = false;
		}
		while (_bars.Count <= index)
		{
			RectTransform rect = UiKit.CreateRect("Beast bar", _canvas);
			rect.sizeDelta = new Vector2(BarWidth, BarHeight);
			Image background = rect.gameObject.AddComponent<Image>();
			background.color = new Color(0f, 0f, 0f, 0.65f);
			background.raycastTarget = false;
			RectTransform fill = UiKit.CreateRect("Fill", rect);
			fill.anchorMin = Vector2.zero;
			fill.anchorMax = Vector2.one;
			fill.offsetMin = new Vector2(1f, 1f);
			fill.offsetMax = new Vector2(-1f, -1f);
			Image fillImage = fill.gameObject.AddComponent<Image>();
			fillImage.color = new Color(0.86f, 0.18f, 0.16f, 1f);
			fillImage.raycastTarget = false;
			_bars.Add(new Bar { Rect = rect, Fill = fill });
		}
		return _bars[index];
	}
}
