using System.Collections.Generic;
using System.Reflection;
using Brix.Components;
using Brix.Engine;
using Brix.Engine.Nature;
using Brix.External.Factories;
using Brix.Legacy;
using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Metallurgy;

// Coal deposits on the surface, like iron's: rocks of coal a harvest task picks (the iron harvest task, as they are
// iron deposits to the game). A coal deposit is an iron deposit whose growth is exactly CoalGrowth (the random world
// puts some by each starting point), so it is saved, loaded, harvested and shown in the harvest task's circle as
// iron is, and no new kind of object is saved. The mod makes the difference:
// - harvesting one drops coal instead of raw iron, and the task expects coal;
// - the iron drawing leaves it out (its texel as for no deposit) and the mod draws its shards itself, with the
//   game's own mineral mesh, placed and sized as the game places iron's, in black coal with a faint sheen.
[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
[HarmonyPatch]
internal class CoalDeposits : MonoBehaviour
{
	// The growth that marks an iron deposit as coal (random worlds' iron has two decimals).
	internal const float CoalGrowth = 0.8137f;

	private const float RefreshSeconds = 1f;

	private static readonly Dictionary<XYZ, GameObject> Shown = new Dictionary<XYZ, GameObject>();

	private static Material _material;

	private float _nextRefresh;

	private static void Enable()
	{
		Plugin.Root.AddComponent<CoalDeposits>();
	}

	internal static bool IsCoal(NatureData nature)
	{
		return nature is IronData && Mathf.Abs(nature.Growth - CoalGrowth) < 0.00005f;
	}

	private static MineralRegistry Irons()
	{
		IronRegistry registry = BrixSingleton<IronRegistry>.Instance;
		return (registry != null) ? registry._internalRegistry : null;
	}

	// Harvested: coal instead of raw iron (as MineralRegistry.Harvest does otherwise).
	[HarmonyPatch(typeof(MineralRegistry), nameof(MineralRegistry.Harvest))]
	[HarmonyPrefix]
	private static bool HarvestCoal(MineralRegistry __instance, XYZ p)
	{
		MineralData mineral = __instance.GetMineral(p);
		if (!IsCoal(mineral))
		{
			return true;
		}
		Garnotte.Nouvelle(mineral.RootPos.ToVector3() + new Vector3(0f, 0.5f, 0f), 1, Metallurgy.CoalKey);
		mineral.CurrentRessources--;
		if (mineral.CurrentRessources <= 0)
		{
			__instance.Break(mineral);
		}
		return false;
	}

	// What the harvesting bricktron expects to carry (room in its hands, a storage that takes it).
	[HarmonyPatch(typeof(NatureData), nameof(NatureData.RepresentativeKey))]
	[HarmonyPostfix]
	private static void CoalKey(NatureData __instance, ref Factory.AssetKey __result)
	{
		if (IsCoal(__instance))
		{
			__result = Metallurgy.CoalKey;
		}
	}

	private void Update()
	{
		if (Time.unscaledTime < _nextRefresh)
		{
			return;
		}
		_nextRefresh = Time.unscaledTime + RefreshSeconds;
		if (!Architecte.commence || Architecte.termine)
		{
			Clear();
			return;
		}
		MineralRegistry irons = Irons();
		if (irons == null)
		{
			return;
		}
		List<XYZ> positions = new List<XYZ>();
		irons.AddAllMineralPositionsToList(ref positions);
		HashSet<XYZ> coal = new HashSet<XYZ>();
		foreach (XYZ position in positions)
		{
			MineralData mineral = irons.GetMineral(position);
			if (!IsCoal(mineral))
			{
				continue;
			}
			coal.Add(position);
			if (!Shown.ContainsKey(position))
			{
				Shown[position] = Build(mineral, position);
			}
		}
		List<XYZ> gone = new List<XYZ>();
		foreach (KeyValuePair<XYZ, GameObject> shown in Shown)
		{
			if (!coal.Contains(shown.Key))
			{
				gone.Add(shown.Key);
			}
		}
		foreach (XYZ position in gone)
		{
			if (Shown[position] != null)
			{
				Destroy(Shown[position]);
			}
			Shown.Remove(position);
		}
	}

	private static void Clear()
	{
		foreach (GameObject go in Shown.Values)
		{
			if (go != null)
			{
				Destroy(go);
			}
		}
		Shown.Clear();
	}

	// The deposit's shards, as MegaVoxelMineralPrinter places iron's.
	private static GameObject Build(MineralData mineral, XYZ position)
	{
		GameObject root = new GameObject("Coal deposit " + position);
		Mesh mesh = ShardMesh();
		if (mesh == null)
		{
			return root;
		}
		List<MineralShardData> shards = new List<MineralShardData>();
		mineral.GetShards(position, ref shards);
		foreach (MineralShardData shard in shards)
		{
			GameObject piece = new GameObject("Shard");
			piece.transform.SetParent(root.transform, false);
			piece.transform.position = mineral.GetShardDisplacement(position, shard);
			piece.transform.rotation = mineral.GetShardOrientation(position, shard) * MegaVoxelMineralPrinter.BaseRotation;
			piece.transform.localScale = Vector3.one * mineral.GetShardScale(position, shard);
			piece.AddComponent<MeshFilter>().sharedMesh = mesh;
			piece.AddComponent<MeshRenderer>().sharedMaterial = CoalMaterial();
		}
		return root;
	}

	private static Mesh ShardMesh()
	{
		GameObject model = Factory.Peek(Brix.External.Factories.Environment.MineralMesh);
		LodMeshFilter filter = (model != null) ? model.GetComponent<LodMeshFilter>() : null;
		return (filter != null) ? filter.lod0Mesh : null;
	}

	// Black, a little blue, with a faint sheen.
	private static Material CoalMaterial()
	{
		if (_material != null)
		{
			return _material;
		}
		foreach (string name in new[] { "Legacy Shaders/Specular", "Standard", "Legacy Shaders/Diffuse", "Diffuse" })
		{
			Shader shader = Shader.Find(name);
			if (shader != null)
			{
				_material = new Material(shader) { name = "Coal deposit" };
				break;
			}
		}
		if (_material == null)
		{
			return null;
		}
		_material.color = new Color(0.09f, 0.09f, 0.1f, 1f);
		if (_material.HasProperty("_SpecColor"))
		{
			_material.SetColor("_SpecColor", new Color(0.45f, 0.45f, 0.5f, 1f));
		}
		if (_material.HasProperty("_Shininess"))
		{
			_material.SetFloat("_Shininess", 0.25f);
		}
		if (_material.HasProperty("_Glossiness"))
		{
			_material.SetFloat("_Glossiness", 0.6f);
		}
		return _material;
	}
}

// The iron drawing leaves coal deposits out: their texel is the one of no deposit (the mod draws them, CoalDeposits).
[Feature(Features.Metallurgy, Features.MetallurgyInfo)]
[HarmonyPatch]
internal static class CoalDepositsHidden
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(MegaVoxelMineralPrinter), "GetColor", new[] { typeof(XYZ), typeof(XYZ), typeof(MineralShardData) });
	}

	private static void Postfix(MegaVoxelMineralPrinter __instance, XYZ position, ref Color32 __result)
	{
		if (__instance is MegaVoxelIronPrinter)
		{
			IronRegistry registry = BrixSingleton<IronRegistry>.Instance;
			if (registry != null && CoalDeposits.IsCoal(registry._internalRegistry.GetMineral(position)))
			{
				__result = default(Color32);
			}
		}
	}
}
