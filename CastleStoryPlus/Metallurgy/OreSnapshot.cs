using System;
using System.Collections.Generic;
using System.Threading;
using Brix.Engine;
using Brix.External.Signals;
using Brix.Utils;

namespace CastleStoryPlus.Metallurgy;

// An ore mine's own copy of the voxels around its sphere (one byte a cell: empty, ore a bricktron may dig), so the
// search for its ore runs on another thread. The game's voxel lookups may only be made on the main thread (they share
// one handle and the block and nature registries change while it runs), so the main thread fills the copy once,
// a slice of rows per frame, and then keeps it up to date from the game's own voxel change signals (the ones its route
// graph follows); every RefillSeconds it is filled again in case a change went by without a signal. A search takes a
// copy of the bytes (some 100 KB for a mine of radius 20) and goes over the sphere on a pool thread with the same rules
// as OreHarvestGoals.Wants, leaving out what needs the main thread (the crew's reach map, trees and boulders); the
// main thread then checks only the cells it found with the full rules before it makes a goal.
internal sealed class OreSnapshot
{
	internal const byte NonEmpty = 1;

	// Terrain ore that is not a block, not invincible: the game would let a bricktron dig it.
	internal const byte Ore = 2;

	// Cells around the sphere the rules look at (a drop of three blocks beside a landing one below the ore).
	private const int Margin = 3;

	private readonly XYZ _min;

	private readonly int _sizeX;

	private readonly int _sizeY;

	private readonly int _sizeZ;

	private readonly byte[] _cells;

	private byte[] _copy;

	private readonly XYZ _centre;

	private readonly int _radius;

	// The next row to fill (y, z); past the last the copy is complete.
	private int _fillY;

	private int _fillZ;

	private SignalConnection _voxelChanged = SignalConnection.Empty;

	private SignalConnection _terrainChanged = SignalConnection.Empty;

	// Set by the pool thread when its search is done; taken by the main thread.
	private volatile List<XYZ> _found;

	private volatile bool _searching;

	internal OreSnapshot(XYZ centre, int radius)
	{
		_centre = centre;
		_radius = radius;
		int reach = radius + Margin;
		_min = new XYZ(centre.x - reach, centre.y - reach, centre.z - reach);
		_sizeX = reach * 2 + 1;
		_sizeY = reach * 2 + 1;
		_sizeZ = reach * 2 + 1;
		_cells = new byte[_sizeX * _sizeY * _sizeZ];
		_fillY = 0;
		_fillZ = 0;
		_voxelChanged = GameSignals.Connect(GameSignals.VoxelChanged, (Action<XYZ>)OnChanged);
		_terrainChanged = GameSignals.Connect(GameSignals.TerrainChanged, (Action<XYZ>)OnChanged);
	}

	internal XYZ Centre => _centre;

	internal int Radius => _radius;

	internal bool Filled => _fillY >= _sizeY;

	internal bool Searching => _searching;

	internal void Dispose()
	{
		_voxelChanged.Disconnect();
		_terrainChanged.Disconnect();
	}

	// Fills the copy from the start again (rows are filled in the frames after).
	internal void Refill()
	{
		_fillY = 0;
		_fillZ = 0;
	}

	// Main thread: fills one row (all x of a y and z); false when the copy is complete.
	internal bool FillRow()
	{
		if (Filled)
		{
			return false;
		}
		for (int x = 0; x < _sizeX; x++)
		{
			XYZ pos = new XYZ(_min.x + x, _min.y + _fillY, _min.z + _fillZ);
			_cells[Index(x, _fillY, _fillZ)] = Read(pos);
		}
		_fillZ++;
		if (_fillZ >= _sizeZ)
		{
			_fillZ = 0;
			_fillY++;
		}
		return !Filled;
	}

	// Main thread: what the game says about a cell now.
	private static byte Read(XYZ pos)
	{
		if (Voxel.IsEmpty(pos))
		{
			return 0;
		}
		byte cell = NonEmpty;
		if (OreMine.IsOre(pos) && Voxel.IsTerrain(pos) && !Voxel.IsInvincible(pos) && !Voxel.IsBlock(pos))
		{
			cell |= Ore;
		}
		return cell;
	}

	// Main thread (the game's signals): a changed cell is read again at once.
	private void OnChanged(XYZ pos)
	{
		int x = pos.x - _min.x;
		int y = pos.y - _min.y;
		int z = pos.z - _min.z;
		if (x < 0 || y < 0 || z < 0 || x >= _sizeX || y >= _sizeY || z >= _sizeZ)
		{
			return;
		}
		_cells[Index(x, y, z)] = Read(pos);
	}

	// Main thread: starts a search over a copy of the cells on a pool thread.
	internal void StartSearch()
	{
		if (_searching || !Filled)
		{
			return;
		}
		_searching = true;
		_found = null;
		// The copy the pool thread reads, reused from search to search (only one runs at a time).
		_copy = _copy ?? new byte[_cells.Length];
		Buffer.BlockCopy(_cells, 0, _copy, 0, _cells.Length);
		byte[] copy = _copy;
		ThreadPool.QueueUserWorkItem(_ =>
		{
			List<XYZ> found;
			try
			{
				found = Search(copy);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("OreMine: the search for ore failed: " + ex.Message);
				found = new List<XYZ>();
			}
			_found = found;
			_searching = false;
		});
	}

	// Main thread: the cells the last search found, once; null while it runs or when taken.
	internal List<XYZ> TakeFound()
	{
		List<XYZ> found = _found;
		if (found != null)
		{
			_found = null;
		}
		return found;
	}

	private int Index(int x, int y, int z)
	{
		return (y * _sizeZ + z) * _sizeX + x;
	}

	// Pool thread: the cells of the sphere that pass OreHarvestGoals.Wants but for what needs the main thread.
	private List<XYZ> Search(byte[] cells)
	{
		var view = new View(this, cells);
		List<XYZ> found = new List<XYZ>();
		int r2 = _radius * _radius;
		for (int dy = -_radius; dy <= _radius; dy++)
		{
			for (int dz = -_radius; dz <= _radius; dz++)
			{
				for (int dx = -_radius; dx <= _radius; dx++)
				{
					if (dx * dx + dy * dy + dz * dz > r2)
					{
						continue;
					}
					XYZ pos = new XYZ(_centre.x + dx, _centre.y + dy, _centre.z + dz);
					if (view.IsOre(pos) && view.HasEmptyNeighbor(pos) && view.HasStandingPlace(pos) && view.SafeToDig(pos))
					{
						found.Add(pos);
					}
				}
			}
		}
		return found;
	}

	// The rules of OreHarvestGoals and Voxel.HasEmptyNeighbor on the copied cells (outside the copy: solid).
	private readonly struct View
	{
		private static readonly XYZ[] Sides = { new XYZ(1, 0, 0), new XYZ(-1, 0, 0), new XYZ(0, 0, 1), new XYZ(0, 0, -1) };

		private readonly OreSnapshot _owner;

		private readonly byte[] _cells;

		internal View(OreSnapshot owner, byte[] cells)
		{
			_owner = owner;
			_cells = cells;
		}

		private byte Cell(XYZ pos)
		{
			int x = pos.x - _owner._min.x;
			int y = pos.y - _owner._min.y;
			int z = pos.z - _owner._min.z;
			if (x < 0 || y < 0 || z < 0 || x >= _owner._sizeX || y >= _owner._sizeY || z >= _owner._sizeZ)
			{
				return NonEmpty;
			}
			return _cells[_owner.Index(x, y, z)];
		}

		internal bool IsOre(XYZ pos)
		{
			return (Cell(pos) & Ore) != 0;
		}

		private bool IsEmpty(XYZ pos)
		{
			return (Cell(pos) & NonEmpty) == 0;
		}

		internal bool HasEmptyNeighbor(XYZ v)
		{
			if (IsEmpty(v + XYZ.up) && IsEmpty(v + XYZ.up + XYZ.up))
			{
				return true;
			}
			if (IsEmpty(v - XYZ.up) && IsEmpty(v - XYZ.up - XYZ.up))
			{
				return true;
			}
			foreach (XYZ side in Sides)
			{
				if (IsEmpty(v + side) && (IsEmpty(v + side + XYZ.up) || IsEmpty(v + side - XYZ.up)))
				{
					return true;
				}
			}
			return false;
		}

		private bool IsEmptyWithout(XYZ cell, XYZ dug)
		{
			return cell == dug || IsEmpty(cell);
		}

		private bool CanStand(XYZ cell, XYZ dug)
		{
			return IsEmptyWithout(cell, dug) && IsEmptyWithout(cell + XYZ.up, dug) && !IsEmptyWithout(cell - XYZ.up, dug);
		}

		// OreHarvestGoals.HasStandingPlace without the reach map.
		internal bool HasStandingPlace(XYZ pos)
		{
			if (CanStand(pos + XYZ.up, pos))
			{
				return true;
			}
			foreach (XYZ side in Sides)
			{
				if (CanStand(pos + side, pos) || CanStand(pos + side - XYZ.up, pos))
				{
					return true;
				}
			}
			return false;
		}

		// OreHarvestGoals.SafeToDig without the reach map.
		internal bool SafeToDig(XYZ pos)
		{
			XYZ landing = pos;
			if (IsEmptyWithout(landing - XYZ.up, pos))
			{
				landing -= XYZ.up;
				if (IsEmptyWithout(landing - XYZ.up, pos))
				{
					return false;
				}
			}
			if (!IsEmptyWithout(landing + XYZ.up, pos))
			{
				return landing == pos;
			}
			bool wayBack = false;
			foreach (XYZ side in Sides)
			{
				XYZ beside = landing + side;
				if (IsEmptyWithout(beside, pos) && IsEmptyWithout(beside - XYZ.up, pos) && IsEmptyWithout(beside - XYZ.up - XYZ.up, pos))
				{
					return false;
				}
				for (int dy = -1; dy <= 1; dy++)
				{
					XYZ step = beside + new XYZ(0, dy, 0);
					if (!CanStand(step, pos))
					{
						continue;
					}
					if (dy == 1 && !IsEmptyWithout(landing + XYZ.up + XYZ.up, pos))
					{
						continue;
					}
					wayBack = true;
				}
			}
			return wayBack;
		}
	}
}
