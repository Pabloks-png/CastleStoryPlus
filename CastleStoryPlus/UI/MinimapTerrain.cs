using System;
using System.Collections;
using System.Collections.Generic;
using Brix.Components;
using Brix.Engine;
using Brix.Engine.Blocks;
using Brix.Engine.FlatWorld.Layers;
using Brix.External.Signals;
using Brix.UI;
using CastleStoryPlus.Core;
using CastleStoryPlus.Loading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CastleStoryPlus.UI;

// The minimap only showed units and crystals on a plain background: its shader draws the terrain only with
// _ShowTerrain, which the game never turns on. The minimap now shows a top-down map of the island: terrain type
// colours (grass, earth, snow, pavement...), lighter the higher it is, shaded slopes, and placed blocks (walls,
// buildings) in stone grey. Units and crystals are drawn on top as before. Built in full now and then (a slice per
// frame so the game does not stutter), and in between only the columns whose voxels changed (dug, filled) are
// looked at again: a full build of a big map read about a million columns at 4 ms a frame most of the time.
[Feature(Features.MinimapTerrain, Features.MinimapTerrainInfo)]
[HarmonyPatch(typeof(MiniMap), nameof(MiniMap.InitHeightmap))]
internal class MinimapTerrain : MonoBehaviour
{
	// A full build every so often (blocks placed or taken down send no voxel change), the changed columns between.
	private const float FullBuildSeconds = 60f;

	private const float ChangesSeconds = 2f;

	private const int MaxSize = 1024;

	private const byte BlockType = 255;

	private static readonly Color32 Background = new Color32(18, 24, 34, 255);

	private static readonly Color BlockColor = new Color(0.62f, 0.6f, 0.56f, 1f);

	private MiniMap _minimap;

	private Texture2D _texture;

	private int _minX;

	private int _minZ;

	private int _size;

	// Reused on every refresh: a 1024 x 1024 map needs ~9 MB of buffers, and allocating them every 15 seconds made
	// the heap grow (Unity's Mono GC does not compact).
	private int[] _heights;

	private byte[] _types;

	private Color32[] _pixels;

	private readonly List<XYZ> _placed = new List<XYZ>();

	private readonly HashSet<XYZ> _tops = new HashSet<XYZ>();

	// Columns (x, 0, z) whose voxels changed since they were last drawn.
	private readonly HashSet<XYZ> _changed = new HashSet<XYZ>();

	private SignalConnection _voxelChanged = SignalConnection.Empty;

	// The heights the colours are shaded by, from the last full build.
	private int _minY;

	private int _maxY;

	private int _topY;

	private static void Postfix(MiniMap __instance)
	{
		if (__instance.mapImage == null || __instance.GetComponent<MinimapTerrain>() != null)
		{
			return;
		}
		__instance.gameObject.AddComponent<MinimapTerrain>()._minimap = __instance;
	}

	private IEnumerator Start()
	{
		Bounds bounds = _minimap.mapBounds;
		_minX = Mathf.FloorToInt(bounds.min.x);
		_minZ = Mathf.FloorToInt(bounds.min.z);
		_size = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bounds.size.x, bounds.size.z)), 1, MaxSize);
		_texture = new Texture2D(_size, _size, TextureFormat.ARGB32, false);
		_texture.filterMode = FilterMode.Bilinear;
		_texture.wrapMode = TextureWrapMode.Clamp;
		_topY = Mathf.CeilToInt(bounds.max.y);
		_voxelChanged = GameSignals.Connect(GameSignals.VoxelChanged, (Action<XYZ>)OnVoxelChanged);
		while (true)
		{
			_changed.Clear();
			yield return Build();
			float next = Time.realtimeSinceStartup + FullBuildSeconds;
			while (Time.realtimeSinceStartup < next)
			{
				yield return new WaitForSecondsRealtime(ChangesSeconds);
				DrawChanged();
			}
		}
	}

	private void OnVoxelChanged(XYZ position)
	{
		_changed.Add(new XYZ(position.x, 0, position.z));
	}

	// The changed columns' top voxel again, and the pixels shaded by them (the column and its east and south
	// neighbours, whose light comes over it).
	private void DrawChanged()
	{
		if (_changed.Count == 0 || _heights == null)
		{
			return;
		}
		foreach (XYZ column in _changed)
		{
			int index = IndexOf(column);
			if (index < 0)
			{
				continue;
			}
			_heights[index] = -1;
			_types[index] = 0;
			for (int y = _topY; y >= 0; y--)
			{
				XYZ voxel = new XYZ(column.x, y, column.z);
				if (Voxel.IsEmpty(voxel))
				{
					continue;
				}
				_heights[index] = y;
				_types[index] = Voxel.IsBlock(voxel) ? BlockType : (byte)Voxel.GetTerrainBlockData(voxel).BlockInfo.ID;
				break;
			}
		}
		foreach (XYZ column in _changed)
		{
			int x = column.x - _minX;
			int z = column.z - _minZ;
			Colorize(x, z);
			Colorize(x + 1, z);
			Colorize(x, z - 1);
		}
		_changed.Clear();
		_texture.SetPixels32(_pixels);
		_texture.Apply();
	}

	private void OnDestroy()
	{
		_voxelChanged.Disconnect();
		if (_texture != null)
		{
			Destroy(_texture);
		}
	}

	private IEnumerator Build()
	{
		int count = _size * _size;
		if (_heights == null || _heights.Length != count)
		{
			_heights = new int[count];
			_types = new byte[count];
			_pixels = new Color32[count];
		}
		int[] heights = _heights;
		byte[] types = _types;
		for (int i = 0; i < count; i++)
		{
			heights[i] = -1;
			types[i] = 0;
		}
		FrameBudget budget = new FrameBudget(4);
		// The game's GetTopVoxelPositionsFromLayers builds a new set of every column's top voxel (~1M on a big map)
		// on each call; fill one kept set instead. The set is a snapshot, so later terrain changes cannot break it.
		_tops.Clear();
		if (Voxel.Engine is LayerVoxelEngine layered && layered.test != null)
		{
			layered.test.GetTopVoxelsFromLayers(_tops);
		}
		else
		{
			_tops.UnionWith(Voxel.Engine.GetTopVoxelPositionsFromLayers());
		}
		foreach (XYZ top in _tops)
		{
			int index = IndexOf(top);
			if (index >= 0 && top.y > heights[index])
			{
				heights[index] = top.y;
				types[index] = (byte)Voxel.GetTerrainBlockData(top).BlockInfo.ID;
			}
			if (budget.Exceeded)
			{
				yield return null;
				budget.Reset();
			}
		}
		BlockEngine blocks = BrixSingleton<BlockEngine>.Instance;
		if (blocks != null && blocks._blocks != null)
		{
			_placed.Clear();
			_placed.AddRange(blocks._blocks.Keys);
			foreach (XYZ block in _placed)
			{
				int index = IndexOf(block);
				if (index >= 0 && block.y >= heights[index])
				{
					heights[index] = block.y;
					types[index] = BlockType;
				}
				if (budget.Exceeded)
				{
					yield return null;
					budget.Reset();
				}
			}
		}
		yield return null;
		_texture.SetPixels32(Colorize(heights, types));
		_texture.Apply();
		Show();
	}

	private int IndexOf(XYZ position)
	{
		int x = position.x - _minX;
		int z = position.z - _minZ;
		if (x < 0 || z < 0 || x >= _size || z >= _size)
		{
			return -1;
		}
		return x + z * _size;
	}

	private Color32[] Colorize(int[] heights, byte[] types)
	{
		_minY = int.MaxValue;
		_maxY = int.MinValue;
		foreach (int height in heights)
		{
			if (height >= 0)
			{
				_minY = Mathf.Min(_minY, height);
				_maxY = Mathf.Max(_maxY, height);
			}
		}
		for (int z = 0; z < _size; z++)
		{
			for (int x = 0; x < _size; x++)
			{
				Colorize(x, z);
			}
		}
		return _pixels;
	}

	private void Colorize(int x, int z)
	{
		if (x < 0 || z < 0 || x >= _size || z >= _size)
		{
			return;
		}
		int index = x + z * _size;
		int height = _heights[index];
		if (height < 0)
		{
			_pixels[index] = Background;
			return;
		}
		Color color = (_types[index] == BlockType) ? BlockColor : Minimap.typeColors[_types[index]];
		float shade = Mathf.Lerp(0.7f, 1.15f, (height - _minY) / (float)Mathf.Max(1, _maxY - _minY));
		// Light from the north-west: slopes facing it are lighter, the others darker; cliffs get an edge.
		int west = (x > 0) ? _heights[index - 1] : height;
		int north = (z < _size - 1) ? _heights[index + _size] : height;
		float slope = (west < 0 ? 0 : height - west) + (north < 0 ? 0 : height - north);
		shade += Mathf.Clamp(slope * 0.05f, -0.3f, 0.3f);
		_pixels[index] = (Color32)new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
	}

	// The game's map material only draws the heightmap data; show the coloured map with the plain UI material.
	private void Show()
	{
		RawImage image = _minimap.mapImage;
		if (image == null)
		{
			return;
		}
		if (image.texture != _texture)
		{
			image.material = null;
			image.texture = _texture;
			image.uvRect = new Rect(0f, 0f, 1f, 1f);
			image.color = Color.white;
		}
	}
}
