using BepInEx.Configuration;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.UI;
using static CastleStoryPlus.UI.UiKit;

namespace CastleStoryPlus.Diagnostics;

// A small frame rate counter at the top centre of the screen: frames per second and the frame time over the last half
// second, in red under 30 fps and in yellow under 60. Switched on and off in the performance window (F7) and kept
// in [Debug] FpsCounter; it shows whether debug mode is on or not.
[Feature(Features.Performance, Features.PerformanceInfo)]
internal sealed class FpsCounter : MonoBehaviour
{
	private const float Interval = 0.5f;

	private static readonly Color Red = new Color(0.95f, 0.35f, 0.3f, 1f);

	private static ConfigEntry<bool> _on;

	private static FpsCounter _instance;

	private GameObject _canvas;

	private Text _text;

	private int _frames;

	private float _seconds;

	internal static bool On => _on != null && _on.Value;

	private static void Enable()
	{
		_on = Plugin.Cfg.Bind("Debug", "FpsCounter", false, "Show a frame rate counter at the top centre of the screen (switched in the performance window, F7).");
		_instance = Plugin.Root.AddComponent<FpsCounter>();
	}

	internal static void Toggle()
	{
		if (_on == null)
		{
			return;
		}
		_on.Value = !_on.Value;
		if (_instance != null)
		{
			_instance.Show(_on.Value);
		}
	}

	private void Start()
	{
		_canvas = CreateCanvas("FpsCounterCanvas", transform, 952);
		GameObject panel = CreatePanel("FpsCounter", _canvas.transform);
		Image back = panel.GetComponent<Image>();
		back.color = new Color(0f, 0f, 0f, 0.55f);
		back.raycastTarget = false;
		SetRect((RectTransform)panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(170f, 28f));
		((RectTransform)panel.transform).pivot = new Vector2(0.5f, 1f);
		_text = CreateText(panel.transform, string.Empty, 16, TextColor, TextAnchor.MiddleCenter);
		SetRect((RectTransform)_text.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		Show(On);
	}

	private void Show(bool visible)
	{
		if (_canvas != null)
		{
			_canvas.SetActive(visible);
		}
		_frames = 0;
		_seconds = 0f;
	}

	private void Update()
	{
		if (_canvas == null || !_canvas.activeSelf)
		{
			return;
		}
		_frames++;
		_seconds += Time.unscaledDeltaTime;
		if (_seconds < Interval)
		{
			return;
		}
		float fps = _frames / _seconds;
		float ms = _seconds * 1000f / _frames;
		_text.text = fps.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " fps   " + ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
		_text.color = (fps < 30f) ? Red : ((fps < 60f) ? Yellow : TextColor);
		_frames = 0;
		_seconds = 0f;
	}
}
