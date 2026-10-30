using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CastleStoryPlus.Port;

// The game files the port is made from, checked against a manifest of SHA-256 hashes (sha256sum format, paths relative
// to "Castle Story_Data", '/' separators). The manifest is made once from a clean Steam install (CreateManifest); before
// converting, every listed file must be present and match, so the port is only ever made from the version it was
// written for. Files the manifest does not list (a mod's leftovers, say) are ignored.
public static class GameFiles
{
	public const string ManifestName = "game-files.sha256";

	// Folders and files of a clean install that the manifest covers.
	private static readonly string[] Skipped = { "Managed.orig/", "output_log.txt", "Player.log" };

	public static void CreateManifest(string dataDir, string manifestPath, Action<string> log)
	{
		var lines = new List<string>();
		foreach (string relative in ListFiles(dataDir))
		{
			lines.Add(Hash(Path.Combine(dataDir, relative)) + "  " + relative);
		}
		File.WriteAllLines(manifestPath, lines);
		log(lines.Count + " files in " + manifestPath);
	}

	// The problems found ("missing <file>", "changed <file>"); empty when the install matches.
	public static List<string> Verify(string dataDir, string manifestPath, Action<int, int> progress)
	{
		var problems = new List<string>();
		string[] lines = File.ReadAllLines(manifestPath).Where(l => l.Length > 66).ToArray();
		for (int i = 0; i < lines.Length; i++)
		{
			string expected = lines[i].Substring(0, 64);
			string relative = lines[i].Substring(66);
			string path = Path.Combine(dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
			progress?.Invoke(i, lines.Length);
			if (!File.Exists(path))
			{
				problems.Add("missing " + relative);
			}
			else if (!string.Equals(Hash(path), expected, StringComparison.OrdinalIgnoreCase))
			{
				problems.Add("changed " + relative);
			}
		}
		progress?.Invoke(lines.Length, lines.Length);
		return problems;
	}

	public static string Hash(string path)
	{
		using (var sha = SHA256.Create())
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
		{
			byte[] hash = sha.ComputeHash(stream);
			var text = new StringBuilder(64);
			foreach (byte b in hash)
			{
				text.Append(b.ToString("x2"));
			}
			return text.ToString();
		}
	}

	private static IEnumerable<string> ListFiles(string dataDir)
	{
		string root = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
			.Select(f => f.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/'))
			.Where(f => !Skipped.Any(s => s.EndsWith("/", StringComparison.Ordinal) ? f.StartsWith(s, StringComparison.Ordinal) : f == s))
			.OrderBy(f => f, StringComparer.Ordinal);
	}
}
