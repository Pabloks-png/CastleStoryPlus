using System;
using System.Collections;
using System.Collections.Generic;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Components.Volumes;
using Brix.Game.Semantique;
using Brix.Transactions;
using CastleStoryPlus.DevTools.Building;
using CastleStoryPlus.DevTools.Endpoints;
using CastleStoryPlus.GameUi;
using CastleStoryPlus.Upgrades;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CastleStoryPlus.DevTools.Worlds;

// The photo studio: one piece of the test world's plan at a time on a fixed, cleared spot beside the test world (west
// of the home crystal), built at once and filled as in the test world (its stockpile's resource and Stacking tier,
// its stand's kits), for pictures of each one alone (tools/make_gallery.py). Showing the next piece takes the last
// one away first: its stockpile emptied, the building and anything loose on the spot destroyed (nothing dropped).
internal partial class TestWorldBuilder
{
	// The studio's middle, from the home crystal.
	private const int StudioOffsetX = -130;

	// Half the width of the spot cleared round the studio's middle.
	private const int StudioClear = 12;

	// The blocks' own spots: rows of this many, this far apart.
	private const int BlockColumns = 8;

	private const int BlockSpacing = 10;

	private List<Piece> _studioPlan;

	private string _studioState = "idle";

	private int _studioIndex = -1;

	private XYZ _studio;

	private Bounds? _studioBounds;

	private bool _studioRunning;

	internal object StudioPlan()
	{
		List<Piece> plan = EnsureStudioPlan();
		List<object> list = new List<object>();
		for (int i = 0; i < plan.Count; i++)
		{
			list.Add(Describe(i, plan[i]));
		}
		return Json.Obj("count", list.Count, "pieces", list);
	}

	internal object StudioStatus()
	{
		Piece piece = (_studioPlan != null && _studioIndex >= 0 && _studioIndex < _studioPlan.Count) ? _studioPlan[_studioIndex] : null;
		return Json.Obj(
			"state", _studioState,
			"running", _studioRunning,
			"spot", (_studioIndex >= 0) ? PathEndpoints.Voxel(_studio) : null,
			"piece", (piece != null) ? Describe(_studioIndex, piece) : null,
			"bounds", _studioBounds.HasValue ? Json.Obj("centre", Floats(_studioBounds.Value.center), "size", Floats(_studioBounds.Value.size)) : null);
	}

	internal object StudioShow(int index)
	{
		if (_running || _studioRunning)
		{
			throw new Api.ApiException(409, "Busy (GET /api/testworld, GET /api/studio)");
		}
		List<Piece> plan = EnsureStudioPlan();
		if (index < 0 || index >= plan.Count)
		{
			throw new Api.ApiException(400, "'index' must be from 0 to " + (plan.Count - 1));
		}
		_studioIndex = index;
		_studioBounds = null;
		_studioRunning = true;
		StartCoroutine(Studio(plan[index]));
		return StudioStatus();
	}

	private List<Piece> EnsureStudioPlan()
	{
		if (_studioPlan == null)
		{
			_studioPlan = Plan(120, out int _, out int _);
		}
		return _studioPlan;
	}

	private static object Describe(int index, Piece piece)
	{
		List<string> kits = null;
		if (piece.Kits != null)
		{
			kits = new List<string>();
			foreach (List<Factory.AssetKey> kit in piece.Kits)
			{
				List<string> names = new List<string>();
				foreach (Factory.AssetKey key in kit)
				{
					names.Add(key.Name);
				}
				kits.Add(string.Join("+", names.ToArray()));
			}
		}
		return Json.Obj(
			"index", index,
			"name", piece.Key.Name,
			"mod", BuildCatalog.Added.Contains(piece.Key.Name),
			"title", Text(piece.Key.Name, (BuildCatalog.Entry e) => e.Name),
			"description", Text(piece.Key.Name, (BuildCatalog.Entry e) => e.Tooltip),
			"section", piece.Section,
			"size", new int[3] { piece.Size.x, piece.Size.y, piece.Size.z },
			"fill", (piece.Fill != null) ? piece.Fill.Name : null,
			"stackingTier", (piece.StackingTier >= 0) ? (int?)piece.StackingTier : null,
			"kits", kits);
	}

	// The build menu's name or description of a blueprint, null when it has none.
	private static string Text(string blueprint, Func<BuildCatalog.Entry, Func<string>> pick)
	{
		foreach (List<BuildCatalog.Entry> list in new[] { BuildCatalog.Structures, BuildCatalog.Blocks })
		{
			BuildCatalog.Entry entry = list.Find((BuildCatalog.Entry e) => e.Key != null && e.Key.Name == blueprint);
			Func<string> text = (entry != null) ? pick(entry) : null;
			if (text != null)
			{
				try
				{
					return text();
				}
				catch (Exception)
				{
					return null;
				}
			}
		}
		return null;
	}

	private static float[] Floats(Vector3 v)
	{
		return new float[3] { v.x, v.y, v.z };
	}

	private IEnumerator Studio(Piece template)
	{
		XYZ saved = _corner;
		try
		{
			XYZ home = WorldEndpoints.HomeVoxel();
			XYZ spot = new XYZ(home.x + StudioOffsetX, 0, home.z);
			if (IsBlock(template.Key))
			{
				// Blocks become terrain and do not come out again cleanly: each one on a fresh spot of its own, in
				// rows north of the studio (the same grass round it).
				int nth = BlockNumber(template);
				spot = new XYZ(home.x + StudioOffsetX - 30 + (nth % BlockColumns) * BlockSpacing, 0, home.z + StudioClear + 12 + (nth / BlockColumns) * BlockSpacing);
			}
			_studio = new XYZ(spot.x, WorldEndpoints.GroundY(spot.x, spot.z) + 1, spot.z);

			_studioState = "clearing";
			ClearStudio();
			// The removals (commands, destroys) go through before anything is placed; the remove tool's blueprints
			// stay behind once their blocks are gone, and go too.
			float cleared = Time.unscaledTime + 0.6f;
			while (Time.unscaledTime < cleared)
			{
				yield return null;
			}
			ClearBlueprints();
			// The place frees up in the game's volume registry a moment after.
			cleared = Time.unscaledTime + 0.6f;
			while (Time.unscaledTime < cleared)
			{
				yield return null;
			}

			// The piece with its footprint in the studio's middle (the test world's placing works from _corner).
			Piece piece = new Piece { Key = template.Key, Size = template.Size, Offset = template.Offset, Section = template.Section, Fill = template.Fill, StackingTier = template.StackingTier, Kits = template.Kits };
			_corner = new XYZ(_studio.x - piece.Size.x / 2, _studio.y, _studio.z - piece.Size.z / 2);
			XYZ at = new XYZ(_corner.x - piece.Offset.x, _corner.y - piece.Offset.y, _corner.z - piece.Offset.z);

			_studioState = "building";
			Project project = ProjectEndpoints.BuildProject(0, out bool _);
			BlueprintPlacer.Result result = null;
			try
			{
				result = BlueprintPlacer.Place(piece.Key, at, 0, project, force: false);
			}
			catch (Exception ex)
			{
				_studioState = "failed (" + ex.GetType().Name + ")";
				yield break;
			}
			// Removed blocks free their place only after a while: tried again every half second, the remove tool's
			// leftover blueprints cleared each time.
			float giveUp = Time.unscaledTime + 12f;
			while (!result.Placed && result.Outcome == "obstructed" && Time.unscaledTime < giveUp)
			{
				float wait = Time.unscaledTime + 0.5f;
				while (Time.unscaledTime < wait)
				{
					yield return null;
				}
				ClearBlueprints();
				yield return null;
				result = BlueprintPlacer.Place(piece.Key, at, 0, project, force: false);
			}
			if (!result.Placed && result.Outcome == "unsupported")
			{
				foreach (Support support in Supports)
				{
					yield return Hold(piece, support, project);
					for (int turns = 0; turns < 4 && !result.Placed; turns++)
					{
						result = BlueprintPlacer.Place(piece.Key, at, turns, project, force: false);
					}
					if (result.Placed)
					{
						break;
					}
				}
			}
			if (!result.Placed)
			{
				_studioState = "not placed (" + result.Outcome + ")";
				yield break;
			}
			InstantBuild.Complete(new List<Blueprint> { result.Blueprint });
			ProjectEndpoints.Commit(project);
			yield return WaitForBuilds();

			if (Object.FindObjectOfType<Market.MarketStation>() != null && piece.Key.Name.IndexOf("Market", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				// Standing trades, so its window has some.
				StandingTrades();
			}
			Vector3 centre = new Vector3(_corner.x + piece.Size.x / 2f, _corner.y, _corner.z + piece.Size.z / 2f);
			Faction faction = User.LocalUser.faction;
			if (piece.Kits != null)
			{
				_studioState = "filling";
				yield return FillStand(Nearest(Object.FindObjectsOfType<Recepteur>(), centre), piece.Kits, faction);
			}
			else if (piece.Fill != null)
			{
				_studioState = "filling";
				Recepteur storage = Nearest(Object.FindObjectsOfType<Recepteur>(), centre);
				if (storage != null)
				{
					if (piece.StackingTier >= 0)
					{
						StockpileTiers.Set(storage, piece.StackingTier);
						// Stacking's room follows within its check (every 3 seconds).
						float until = Time.unscaledTime + 4f;
						while (Time.unscaledTime < until)
						{
							yield return null;
						}
					}
					int stored = 0;
					while (stored < MaxFill && storage.HasRoomFor(piece.Fill))
					{
						GameObject item = Transactor.SpawnUNet(piece.Fill, faction, storage.transform.position + Vector3.up, Quaternion.identity);
						if (item == null)
						{
							break;
						}
						storage.AddObject(item);
						stored++;
						if (stored % SpawnPerFrame == 0)
						{
							yield return null;
						}
					}
				}
			}
			// The displays catch up (a stockpile's content, the stand's rows).
			float settle = Time.unscaledTime + 1.5f;
			while (Time.unscaledTime < settle)
			{
				yield return null;
			}
			// A block is part of the terrain then (no renderer of its own): the columns it raised above the grass.
			_studioBounds = IsBlock(piece.Key) ? RaisedBounds(piece) : DrawnBounds(Object.FindObjectsOfType<Renderer>(), piece);
			_studioState = "ready";
		}
		finally
		{
			_corner = saved;
			_studioRunning = false;
		}
	}

	// What stands above the grass round the studio's spot, by its columns (a voxel's middle is on its whole
	// coordinates); null when nothing does.
	private Bounds? RaisedBounds(Piece piece)
	{
		int grass = _studio.y - 1;
		int reach = Mathf.Max(piece.Size.x, piece.Size.z) + 3;
		int minX = int.MaxValue;
		int minZ = int.MaxValue;
		int maxX = int.MinValue;
		int maxZ = int.MinValue;
		int top = grass;
		for (int x = _studio.x - reach; x <= _studio.x + reach; x++)
		{
			for (int z = _studio.z - reach; z <= _studio.z + reach; z++)
			{
				int y = WorldEndpoints.GroundY(x, z);
				if (y == int.MinValue || y <= grass || y > grass + 20)
				{
					continue;
				}
				minX = Mathf.Min(minX, x);
				maxX = Mathf.Max(maxX, x);
				minZ = Mathf.Min(minZ, z);
				maxZ = Mathf.Max(maxZ, z);
				top = Mathf.Max(top, y);
			}
		}
		if (top == grass)
		{
			return null;
		}
		Vector3 low = new Vector3(minX - 0.5f, grass + 0.5f, minZ - 0.5f);
		Vector3 high = new Vector3(maxX + 0.5f, top + 0.5f, maxZ + 0.5f);
		return new Bounds((low + high) / 2f, high - low);
	}

	// Everything of the player's on the spot goes: stockpiles emptied first, then the buildings and the loose items
	// destroyed (not demolished: nothing is dropped or paid back). Blocks are not on it (they get spots of their own).
	private void ClearStudio()
	{
		Faction faction = User.LocalUser.faction;
		List<GameObject> gone = new List<GameObject>();
		foreach (MovableVolume volume in Object.FindObjectsOfType<MovableVolume>())
		{
			GameObject building = volume.gameObject;
			// Blocks are left to the remove tool below: destroying a block's object leaves its voxels behind.
			if (!OnStudio(building.transform.position) || !CastleStoryPlus.Building.CopyPaste.IsPlacedBuilding(volume) || !faction.IsSame(building))
			{
				continue;
			}
			foreach (Recepteur storage in building.GetComponentsInChildren<Recepteur>(true))
			{
				foreach (IDescriptor item in new List<IDescriptor>(storage.StoredItems))
				{
					if (item != null && item.GameObject != null)
					{
						gone.Add(item.GameObject);
					}
				}
			}
			gone.Add(building);
		}
		foreach (Rigidbody body in Object.FindObjectsOfType<Rigidbody>())
		{
			if (body != null && OnStudio(body.transform.position) && body.GetComponent<IDescriptor>() != null && body.GetComponent<MovableVolume>() == null && body.GetComponent<Labor>() == null)
			{
				gone.Add(body.gameObject);
			}
		}
		foreach (GameObject go in gone)
		{
			if (go != null)
			{
				go.SafeNetworkDestroy();
			}
		}
	}

	// The block's number among the plan's blocks (its spot).
	private int BlockNumber(Piece piece)
	{
		int nth = 0;
		foreach (Piece other in EnsureStudioPlan())
		{
			if (other == piece)
			{
				return nth;
			}
			if (IsBlock(other.Key))
			{
				nth++;
			}
		}
		return nth;
	}

	// Blueprints left on the spot (the remove tool's, once done, or a piece that could not be finished).
	private void ClearBlueprints()
	{
		foreach (Blueprint blueprint in Object.FindObjectsOfType<Blueprint>())
		{
			if (blueprint != null && OnStudio(blueprint.transform.position))
			{
				blueprint.gameObject.SafeNetworkDestroy();
			}
		}
	}

	// A block of the build menu (or the floor block the supports are made of), not a building.
	private static bool IsBlock(Factory.AssetKey key)
	{
		foreach (BuildCatalog.Entry entry in BuildCatalog.Blocks)
		{
			if (entry.Key != null && entry.Key.Name == key.Name)
			{
				return true;
			}
		}
		Factory.AssetKey floor = FloorBlock();
		return floor != null && floor.Name == key.Name;
	}

	private bool OnStudio(Vector3 position)
	{
		return Mathf.Abs(position.x - _studio.x) <= StudioClear && Mathf.Abs(position.z - _studio.z) <= StudioClear && position.y >= _studio.y - 2 && position.y <= _studio.y + 30;
	}
}
