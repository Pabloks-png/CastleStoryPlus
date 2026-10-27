using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Brix.Engine;
using Brix.External.Factories;
using Brix.Game.Components.Volumes;
using Brix.Game.Factories;
using CastleStoryPlus.DevTools.Api;
using HarmonyLib;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// The block shapes of every factory asset, written to a file: for rebuilding the game's factory scenes elsewhere,
// where these could not be read back from the game's files. The factories' VolumeDataComponents are destroyed
// once read at start (VolumePreprocessorComponent.PostProcess), so the shapes come from the preprocessor's copy:
// rotation 0 (none), first configuration.
internal static class VolumeDumpEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("POST", "/api/volumes/dump", "Writes the unrotated volume data (cells and their properties) and registry mode of every factory asset the volume preprocessor holds to a JSON file (in a game)", Dump)
		{
			Parameters = "file (default BepInEx/volumedump.json)"
		});
	}

	private static object Dump(ApiRequest request)
	{
		VolumePreprocessorComponent preprocessor = VolumePreprocessor._instance;
		if (preprocessor == null)
		{
			throw new ApiException(409, "No volume preprocessor: start or load a game first");
		}
		string file = request.String("file", Path.Combine(Paths.BepInExRootPath, "volumedump.json"));
		object container = Traverse.Create(preprocessor).Field("_preprocessedData").GetValue();
		IDictionary settings = (IDictionary)Traverse.Create(container).Field("_vSettings").GetValue();
		StringBuilder json = new StringBuilder("[\n");
		int written = 0;
		int cells = 0;
		Dictionary<string, int> perFactory = new Dictionary<string, int>();
		foreach (DictionaryEntry entry in settings)
		{
			Traverse profile = Traverse.Create(entry.Key);
			if (profile.Field("_rotationIndex").GetValue<int>() != 0)
			{
				continue;
			}
			Factory.AssetKey key = profile.Field("_assetKey").GetValue<Factory.AssetKey>();
			IList configurations = (IList)entry.Value;
			if (key == null || configurations == null || configurations.Count == 0)
			{
				continue;
			}
			Dictionary<XYZ, VPropertyMask> shape = (Dictionary<XYZ, VPropertyMask>)configurations[0];
			if (written > 0)
			{
				json.Append(",\n");
			}
			json.Append("{\"factory\":").Append(Quote(key.Factory));
			json.Append(",\"name\":").Append(Quote(key.Name));
			json.Append(",\"configurations\":").Append(configurations.Count);
			json.Append(",\"registryMode\":").Append(RegistryMode(key));
			StringBuilder keys = new StringBuilder();
			StringBuilder values = new StringBuilder();
			int count = 0;
			foreach (KeyValuePair<XYZ, VPropertyMask> cell in shape)
			{
				if (count > 0)
				{
					keys.Append(',');
					values.Append(',');
				}
				keys.Append('[').Append(cell.Key.x).Append(',').Append(cell.Key.y).Append(',').Append(cell.Key.z).Append(']');
				values.Append(((long)cell.Value).ToString(CultureInfo.InvariantCulture));
				count++;
			}
			json.Append(",\"keys\":[").Append(keys).Append("],\"values\":[").Append(values).Append("]}");
			written++;
			cells += count;
			perFactory.TryGetValue(key.Factory ?? string.Empty, out int n);
			perFactory[key.Factory ?? string.Empty] = n + 1;
		}
		json.Append("\n]\n");
		File.WriteAllText(file, json.ToString());
		return Obj("file", file, "assets", written, "cells", cells, "perFactory", perFactory);
	}

	// The registry mode the template's VolumeDataComponent had: the preprocessor hands it to the MovableVolume it
	// adds in its place. -1 when the template has a volume of its own (no MovableVolume).
	private static int RegistryMode(Factory.AssetKey key)
	{
		GameObject template = Factory.Peek(key);
		MovableVolume volume = (template != null) ? template.GetComponent<MovableVolume>() : null;
		return (volume != null) ? (int)volume.RegistryMode : -1;
	}

	private static string Quote(string text)
	{
		StringBuilder result = new StringBuilder("\"");
		foreach (char c in text ?? string.Empty)
		{
			if (c == '"' || c == '\\')
			{
				result.Append('\\').Append(c);
			}
			else if (c < ' ')
			{
				result.Append("\\u").Append(((int)c).ToString("x4"));
			}
			else
			{
				result.Append(c);
			}
		}
		return result.Append('"').ToString();
	}
}
