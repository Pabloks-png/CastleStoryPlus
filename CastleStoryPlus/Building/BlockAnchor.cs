using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Game.Factories;
using Brix.Input;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Building;

// In build mode X picks which cell of the held block goes where the mouse points: each press the next of the block's
// cells (bottom layer first, row by row), Shift+X the one before, and round again to the block's own spot. Choosing
// another block (or leaving build mode) starts again from the block's own spot; rotating keeps the choice. Works for
// every block and structure, and for each block of a dragged row. The game's "back to work" on X does not fire while
// placing. The mod's corner key for its own bricks (BlockAnchorKey) and this one undo each other.
[Feature(Features.BlockAnchor, Features.BlockAnchorInfo)]
internal static class BlockAnchor
{
	private static ConfigEntry<KeyCode> _key;

	// 0: the block's own spot; n: the n-th cell after it in the block's cell order (wrapping round).
	private static int _anchor;

	private static Factory.AssetKey _heldKey;

	private static bool _enabled;

	private static void Enable()
	{
		_enabled = true;
		_key = Plugin.Cfg.Bind("Building", "PlacementAnchorKey", KeyCode.X, "In build mode: place the held block by its next cell (Shift: the one before). Choosing another block starts again.");
		GameSession.OnLeave(Reset);
	}

	internal static bool Active => _enabled && _anchor != 0 && InputModeController._mode == InputMode.blueprint;

	private static void Reset()
	{
		_anchor = 0;
		_heldKey = null;
	}

	internal static void ClearAnchor()
	{
		_anchor = 0;
	}

	// Every frame from InputModeController.Update.
	internal static void Update()
	{
		if (InputModeController._mode != InputMode.blueprint)
		{
			Reset();
			return;
		}
		Factory.AssetKey held = HeldKey();
		if (held != _heldKey)
		{
			_heldKey = held;
			_anchor = 0;
		}
		if (CustomBlocks.AnchorKey != null && Input.GetKeyDown(CustomBlocks.AnchorKey.Value))
		{
			_anchor = 0;
		}
		if (held == null || !Input.GetKeyDown(_key.Value) || InputModeController.IsPaused() || InputModeController.IsInMenu())
		{
			return;
		}
		if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
		{
			return;
		}
		bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		_anchor += back ? -1 : 1;
		// The mod's own bricks place by their chosen corner: back to their own spot, so the two do not add up.
		CustomBlocks.Anchor = 0;
	}

	private static Factory.AssetKey HeldKey()
	{
		Picker picker = (Picking.Instance != null) ? Picking.Instance.OverridePicker : null;
		GrabberComponentV2 grabber = (picker != null) ? picker.mainComponent as GrabberComponentV2 : null;
		return (grabber != null) ? grabber.GetKey() : null;
	}

	// The fitter has put the ghost with its own spot (the pivot cell) at the cell the mouse points to: moves it so the
	// chosen cell is there instead.
	internal static void Shift(Component fitter)
	{
		if (!Active || fitter == null)
		{
			return;
		}
		Transform transform = fitter.transform;
		IVolume volume = fitter.GetComponent<IVolume>();
		Factory.AssetKey key = (volume != null) ? volume.AssetKey : null;
		if (key == null)
		{
			return;
		}
		List<XYZ>[] indices = VolumePreprocessor.GetVSettingsIndices(key, transform.rotation);
		List<XYZ> body = (indices != null && indices.Length > (int)VProperty.fantome) ? indices[(int)VProperty.fantome] : null;
		if (body == null || body.Count == 0)
		{
			return;
		}
		List<XYZ> cells = new List<XYZ>(body);
		if (!cells.Contains(XYZ.zero))
		{
			cells.Add(XYZ.zero);
		}
		cells.Sort((XYZ a, XYZ b) => (a.y != b.y) ? a.y.CompareTo(b.y) : (a.z != b.z) ? a.z.CompareTo(b.z) : a.x.CompareTo(b.x));
		int count = cells.Count;
		int start = cells.IndexOf(XYZ.zero);
		int index = ((start + _anchor) % count + count) % count;
		XYZ cell = cells[index];
		transform.position -= cell.ToVector3();
	}
}

// Every fitter's placing of a ghost (a single block, the start of a drag and each block of a dragged row). A fitter
// that calls its base fitter is shifted once, when the outermost call ends.
[Feature(Features.BlockAnchor, Features.BlockAnchorInfo)]
[HarmonyPatch]
internal static class BlockAnchorFitPatch
{
	[ThreadStatic]
	private static int _depth;

	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type[] parameters = { typeof(VoxelRaycastHit), typeof(Vector3).MakeByRefType() };
		List<MethodBase> methods = new List<MethodBase>();
		foreach (Type type in AllFitterTypes())
		{
			foreach (string name in new[] { "FitAndOrient", "FitForDrag" })
			{
				MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, parameters, null);
				if (method != null && !method.IsAbstract)
				{
					methods.Add(method);
				}
			}
		}
		return methods;
	}

	private static IEnumerable<Type> AllFitterTypes()
	{
		List<Type> types = new List<Type>();
		foreach (Assembly assembly in new[] { typeof(AbstractFitter).Assembly, typeof(CustomBlockFitter).Assembly })
		{
			Type[] all;
			try
			{
				all = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				all = ex.Types;
			}
			foreach (Type type in all)
			{
				if (type != null && typeof(AbstractFitter).IsAssignableFrom(type))
				{
					types.Add(type);
				}
			}
		}
		return types;
	}

	private static void Prefix()
	{
		_depth++;
	}

	private static void Postfix(Component __instance)
	{
		if (_depth != 1)
		{
			return;
		}
		try
		{
			BlockAnchor.Shift(__instance);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("BlockAnchor: " + ex.Message);
		}
	}

	private static void Finalizer()
	{
		_depth--;
	}
}

[Feature(Features.BlockAnchor, Features.BlockAnchorInfo)]
[HarmonyPatch(typeof(InputModeController), nameof(InputModeController.Update))]
internal static class BlockAnchorInputPatch
{
	private static void Prefix()
	{
		try
		{
			BlockAnchor.Update();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("BlockAnchor: " + ex.Message);
		}
	}
}

// The game's "back to work" (X) stays quiet while a block is held for placing.
[Feature(Features.BlockAnchor, Features.BlockAnchorInfo)]
[HarmonyPatch(typeof(Brix.Utils.UI.KeyBindingsUtility), nameof(Brix.Utils.UI.KeyBindingsUtility.RegisterAction))]
internal static class BlockAnchorBackToWorkPatch
{
	private static void Prefix(string actionName, ref Action<Rewired.InputActionEventData> action)
	{
		if (actionName != "command_BackToWork")
		{
			return;
		}
		Action<Rewired.InputActionEventData> inner = action;
		action = (Rewired.InputActionEventData data) =>
		{
			if (InputModeController._mode != InputMode.blueprint)
			{
				inner(data);
			}
		};
	}
}
