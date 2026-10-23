using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CastleStoryPlus.WorldGen;

// Writes a generated world as an ordinary map folder (the files of the game's own maps, version 25), so the game
// lists, validates and loads it like any other map:
// - Heightmap.png: per column red = bottom, green = top (exclusive), blue = top voxel type, alpha = top voxel fill;
//   picture row r is world z = 1023 - r;
// - trees / plants / boulders / iron / pyrite json, standing on the top voxel;
// - gameobjects.json: the blank map's objects (from the game's "New Map" templates) plus a WorldEditorMarker per
//   marker, and LuaMarkersCount.json, the copy of the markers the gamemode validators read;
// - meta.json and a thumbnail drawn from above.
internal static class MapWriter
{
	private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	internal static void Write(GeneratedWorld world, string folder, string codename, string displayName, string description, string gameDir)
	{
		Directory.CreateDirectory(folder);
		File.WriteAllBytes(Path.Combine(folder, "Heightmap.png"), Heightmap(world));
		File.WriteAllBytes(Path.Combine(folder, "thumbnail.png"), Thumbnail.Encode(world));
		File.WriteAllText(Path.Combine(folder, "meta.json"), Meta(world, codename, displayName, description));
		File.WriteAllText(Path.Combine(folder, "trees.json"), Trees(world));
		File.WriteAllText(Path.Combine(folder, "plants.json"), Plants(world));
		File.WriteAllText(Path.Combine(folder, "boulders.json"), Boulders(world));
		File.WriteAllText(Path.Combine(folder, "iron.json"), Minerals(world.Iron));
		File.WriteAllText(Path.Combine(folder, "pyrite.json"), Minerals(world.Pyrite));
		File.WriteAllText(Path.Combine(folder, "TerrainSurfaceLayer.json"), "{\"addedSet\":{\"points\":[]},\"pResourcesArray\":[]}");
		File.WriteAllText(Path.Combine(folder, "PlacedBlocks.json"), "[]");
		File.WriteAllText(Path.Combine(folder, "LuaMarkersCount.json"), MarkerCounts(world));
		File.WriteAllText(Path.Combine(folder, "gameobjects.json"), GameObjects(world, codename, gameDir));
	}

	private static byte[] Heightmap(GeneratedWorld world)
	{
		const int N = GeneratedWorld.Size;
		byte[] rgba = new byte[N * N * 4];
		for (int z = 0; z < N; z++)
		{
			int row = (N - 1 - z) * N * 4;
			for (int x = 0; x < N; x++)
			{
				int i = x + z * N;
				if (world.Top[i] == 0)
				{
					continue;
				}
				int o = row + x * 4;
				rgba[o] = world.Bottom[i];
				rgba[o + 1] = world.Top[i];
				rgba[o + 2] = world.Crust[i];
				rgba[o + 3] = world.Fill[i];
			}
		}
		return PngWriter.Encode(rgba, N, N);
	}

	private static string Meta(GeneratedWorld world, string codename, string displayName, string description)
	{
		StringBuilder s = new StringBuilder();
		s.Append("{\n");
		Field(s, "__CODENAME", Quote(codename));
		Field(s, "__DISPLAYNAME", Quote(displayName));
		Field(s, "__CUSTOMNAME", Quote(string.Empty));
		Field(s, "__DESCRIPTION", Quote(description));
		Field(s, "__MAPTYPE", Quote("Map"));
		Field(s, "__SAVETYPE", Quote("Map"));
		Field(s, "__ASSETVERSION", "25");
		Field(s, "__MAPSIZE", Quote(world.Settings.Size.ToString()));
		Field(s, "__BIOMETYPE", Quote(world.Settings.Biome.ToString()));
		Field(s, "__SUPPORTED_GAMEMODES", "{ \"$type\": \"System.String[], mscorlib\", \"$values\": [] }");
		Field(s, "__RESTRICT_GAMEMODES", "false");
		Field(s, "__LASTPLAYED_GAMEMODE", Quote(string.Empty));
		Field(s, "__LASTPLAYED_GAMEMODEPRESET", Quote("None"));
		Field(s, "__PLAYABLECONTEXT", "7");
		Field(s, "__ISDEBUG", "false");
		Field(s, "__RATING", "0.5");
		Field(s, "__WORKSHOPID", "0");
		Field(s, "__WORKSHOPTAGS", "0");
		Field(s, "__CREATIONTIME", "0");
		s.Append("  \"__MODIFIEDTIME\": 0\n}\n");
		return s.ToString();
	}

	private static void Field(StringBuilder s, string key, string value)
	{
		s.Append("  ").Append(Quote(key)).Append(": ").Append(value).Append(",\n");
	}

	private static string Trees(GeneratedWorld world)
	{
		StringBuilder s = new StringBuilder("[");
		for (int i = 0; i < world.Trees.Count; i++)
		{
			GeneratedWorld.Tree t = world.Trees[i];
			s.Append((i == 0) ? "\n" : ",\n").Append("{\"x\":").Append(t.X).Append(",\"y\":").Append(t.Y).Append(",\"z\":").Append(t.Z).Append(",\"s\":").Append(t.Size).Append('}');
		}
		return s.Append("\n]").ToString();
	}

	private static string Plants(GeneratedWorld world)
	{
		StringBuilder s = new StringBuilder("[");
		for (int i = 0; i < world.Plants.Count; i++)
		{
			GeneratedWorld.Plant p = world.Plants[i];
			s.Append((i == 0) ? "\n" : ",\n").Append("{\"x\":").Append(p.X).Append(",\"y\":").Append(p.Y).Append(",\"z\":").Append(p.Z).Append(",\"s\":").Append(Number(p.Growth)).Append(",\"t\":").Append(p.Type).Append('}');
		}
		return s.Append("\n]").ToString();
	}

	// The game keeps only the number of a boulder's positions (its size), so they are written empty.
	private static string Boulders(GeneratedWorld world)
	{
		StringBuilder s = new StringBuilder("[");
		for (int i = 0; i < world.Boulders.Count; i++)
		{
			GeneratedWorld.Boulder b = world.Boulders[i];
			s.Append((i == 0) ? "\n" : ",\n").Append("{\"positions\":[");
			for (int k = 0; k < b.Cells; k++)
			{
				s.Append((k == 0) ? "{}" : ",{}");
			}
			s.Append("],\"displayPos\":{\"x\":").Append(Number(b.X)).Append(",\"y\":").Append(Number(b.Y)).Append(",\"z\":").Append(Number(b.Z));
			s.Append("},\"offset\":{\"x\":").Append(Number(b.OffsetX)).Append(",\"z\":").Append(Number(b.OffsetZ));
			s.Append("},\"growth\":").Append(Number(b.Growth)).Append(",\"variation\":").Append((b.Cells == 9) ? 2 : 1).Append('}');
		}
		return s.Append("\n]").ToString();
	}

	private static string Minerals(System.Collections.Generic.List<GeneratedWorld.Mineral> minerals)
	{
		StringBuilder s = new StringBuilder("[");
		for (int i = 0; i < minerals.Count; i++)
		{
			GeneratedWorld.Mineral m = minerals[i];
			s.Append((i == 0) ? "\n" : ",\n").Append("{\"position\":{\"x\":").Append(m.X).Append(",\"y\":").Append(m.Y).Append(",\"z\":").Append(m.Z).Append("},\"growth\":").Append(Number(m.Growth)).Append('}');
		}
		return s.Append("\n]").ToString();
	}

	private static string MarkerCounts(GeneratedWorld world)
	{
		StringBuilder s = new StringBuilder("[");
		for (int i = 0; i < world.Markers.Count; i++)
		{
			GeneratedWorld.Marker m = world.Markers[i];
			string position = "{\"$type\":\"UnityEngine.Vector3, UnityEngine\",\"x\":" + m.X + ".0,\"y\":" + m.Y + ".0,\"z\":" + m.Z + ".0}";
			string rotation = "{\"$type\":\"UnityEngine.Quaternion, UnityEngine\",\"w\":1.0}";
			s.Append((i == 0) ? "\n" : ",\n");
			s.Append("{\"markerTag\":\"unset\",\"markerGroups\":{\"$type\":\"System.Collections.Generic.List`1[[System.String, mscorlib]], mscorlib\",\"$values\":[").Append(Quote(m.Group)).Append("]},");
			s.Append("\"position\":").Append(position).Append(",\"localPosition\":").Append(position).Append(',');
			s.Append("\"rotation\":").Append(rotation).Append(",\"localRotation\":").Append(rotation).Append('}');
		}
		return s.Append("\n]").ToString();
	}

	// The blank map's objects (camera, sky, clock, rules, factions, Lua registry...) followed by the markers.
	private static string GameObjects(GeneratedWorld world, string codename, string gameDir)
	{
		string template = Template(gameDir).TrimEnd();
		if (!template.EndsWith("]"))
		{
			throw new InvalidDataException("unexpected gameobjects.json template");
		}
		StringBuilder s = new StringBuilder(template.Substring(0, template.Length - 1));
		Random ids = new Random(world.Settings.SeedValue);
		int[] numbers = new int[3];
		for (int i = 0; i < world.Markers.Count; i++)
		{
			GeneratedWorld.Marker m = world.Markers[i];
			string group = m.Group;
			int number = ++numbers[(group == WorldGenerator.StartGroup) ? 0 : ((group == WorldGenerator.CrystalGroup) ? 1 : 2)];
			string name = Label(group) + " ( " + number + " )";
			string marker = "go:" + Id(ids);
			string beacon = "go:" + Id(ids);
			s.Append(",{\n");
			s.Append("  \"$id\": ").Append(Quote(marker)).Append(",\n");
			s.Append("  \"$type\": \"UnityEngine.GameObject, UnityEngine\",\n");
			s.Append("  \"$assetKey\": \"Balises.WorldEditorMarker\",\n");
			s.Append("  \"$path\": \"/Jeu/Metadata\",\n");
			s.Append("  \"name\": ").Append(Quote("WorldEditorMarker (" + i + ")")).Append(",\n");
			s.Append("  \"components\": [\n");
			s.Append("    {\"$type\": \"UnityEngine.Transform, UnityEngine\", \"position\": {\"$type\": \"UnityEngine.Vector3, UnityEngine\", \"x\": ").Append(m.X).Append(".0, \"y\": ").Append(m.Y).Append(".0, \"z\": ").Append(m.Z).Append(".0}},\n");
			s.Append("    {\"$type\": \"Brix.Game.Components.Blueprint, Assembly-CSharp-firstpass\", \"_isAnti\": false, \"commited\": false},\n");
			s.Append("    {\"$type\": \"Brix.Game.Beacons.BeaconDriver, Assembly-CSharp-firstpass\", \"nameOverride\": ").Append(Quote(name)).Append(", \"Beacon\": {\n");
			s.Append("      \"$component\": \"Brix.Legacy.Beacon, Assembly-CSharp-firstpass\",\n");
			s.Append("      \"$id\": ").Append(Quote(beacon)).Append(",\n");
			s.Append("      \"$type\": \"UnityEngine.GameObject, UnityEngine\",\n");
			s.Append("      \"$assetKey\": \"Balises.MarkerBeacon\",\n");
			s.Append("      \"$parent\": ").Append(Quote(marker)).Append(",\n");
			s.Append("      \"$path\": ").Append(Quote("/Jeu/Metadata/" + marker)).Append(",\n");
			s.Append("      \"name\": ").Append(Quote(name)).Append(",\n");
			s.Append("      \"components\": [{\"$type\": \"Brix.Legacy.Beacon, Assembly-CSharp-firstpass\", \"VolumeComponent\": null, \"radius\": 5.0, \"position\": {\"$type\": \"Brix.Engine.XYZ, Brix.Core\", \"x\": ").Append(m.X).Append(", \"y\": ").Append(m.Y).Append(", \"z\": ").Append(m.Z).Append("}}]\n");
			s.Append("    }},\n");
			s.Append("    {\"$type\": \"Brix.Lua.LuaMarker, Assembly-CSharp-firstpass\", \"markerTag\": \"unset\", \"markerGroups\": [").Append(Quote(group)).Append("], \"DisplayName\": ").Append(Quote(name)).Append(", \"Placed\": true}\n");
			s.Append("  ]\n");
			s.Append("},{\n  \"$ref\": ").Append(Quote(beacon)).Append("\n}");
		}
		return s.Append("]\n").ToString();
	}

	// The game's own blank map (the world editor's "New Map" templates have the same objects).
	private static string Template(string gameDir)
	{
		foreach (string name in new[] { "New Map Forest", "New Map Winter", "New Map Desert" })
		{
			string path = Path.Combine(Path.Combine(Path.Combine(gameDir, "Info/Maps"), name), "gameobjects.json");
			if (File.Exists(path))
			{
				return File.ReadAllText(path);
			}
		}
		throw new FileNotFoundException("no blank map template (Info/Maps/New Map Forest/gameobjects.json)");
	}

	private static string Label(string group)
	{
		if (group == WorldGenerator.StartGroup)
		{
			return "Crystal - Player Spawn";
		}
		return (group == WorldGenerator.CrystalGroup) ? "Crystal - Neutral" : "Corruptron Spawn";
	}

	private static string Id(Random random)
	{
		byte[] bytes = new byte[16];
		random.NextBytes(bytes);
		return new Guid(bytes).ToString();
	}

	private static string Number(float value)
	{
		return value.ToString("0.0###", Invariant);
	}

	private static string Quote(string text)
	{
		return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	}
}
