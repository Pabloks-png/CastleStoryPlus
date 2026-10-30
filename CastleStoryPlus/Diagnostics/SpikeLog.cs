using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using Brix.Assets;
using CastleStoryPlus.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleStoryPlus.Diagnostics;

// Every spike (FrameMonitor) as one JSON line in BepInEx/castlestoryplus_spikes.jsonl, for statistics: when, frame,
// milliseconds, garbage collections, kilobytes allocated, game speed, scene, map and game mode, the time no measured
// system covers, and the measured systems that took the most. With [Performance] SpikeLog the systems are measured
// from the start (FrameMonitor.StartDetailed), so every line says what took the time.
// Per system the spikes are also summed up: how many spikes it was the biggest part of, its milliseconds in spikes
// (total, average, max); "garbage collection" and "unmeasured" count as systems of their own. The summary, biggest
// total first (where cutting time removes the most stutter), goes to the log and to
// BepInEx/castlestoryplus_spikes_summary.txt every SummaryMinutes and when a game is left, and as a "summary" line
// into the JSON file. Spikes while the game runs fast (above 5x, long frames by design) are written with their speed
// but left out of the summary. The file starts again at MaxFileBytes (the old one kept as .old).
internal sealed class SpikeLog : MonoBehaviour
{
	private const string FileName = "castlestoryplus_spikes.jsonl";

	private const string SummaryName = "castlestoryplus_spikes_summary.txt";

	private const long MaxFileBytes = 20L * 1024 * 1024;

	private const float FlushSeconds = 5f;

	private const float SummaryMinutes = 10f;

	private const float QuietAboveTimeScale = 5f;

	private const string GcName = "garbage collection";

	private const string UnmeasuredName = "unmeasured";

	private sealed class Total
	{
		public string Name;

		public int Biggest;

		public int Spikes;

		public double Ms;

		public float MaxMs;
	}

	internal static ConfigEntry<bool> Enabled;

	private static readonly Dictionary<string, Total> Totals = new Dictionary<string, Total>();

	private static StreamWriter _writer;

	private static int _spikes;

	private static double _spikeMs;

	private static int _sinceSummary;

	private static float _nextFlush;

	private static float _nextSummary;

	private static DateTime _since = DateTime.Now;

	internal static void Bind()
	{
		Enabled = Plugin.Cfg.Bind("Performance", "SpikeLog", false, "Write every lag spike, with the systems that took the time, to BepInEx/castlestoryplus_spikes.jsonl and sum them up per system (castlestoryplus_spikes_summary.txt and the log every 10 minutes and when a game is left). Measures the systems from the start: a short freeze once while it patches, and about 1.3 ms every frame in a big base (some 380 timed methods), so leave it off unless hunting spikes.");
		if (!Enabled.Value)
		{
			return;
		}
		Plugin.Root.AddComponent<SpikeLog>();
		GameSession.OnLeave(() => Summarize("game left"));
	}

	// One spike of FrameMonitor (systems: the biggest buckets, already sorted).
	internal static void Add(FrameMonitor.Spike spike, float allocKb)
	{
		if (Enabled == null || !Enabled.Value)
		{
			return;
		}
		float speed = Time.timeScale;
		try
		{
			Write(Line(spike, allocKb, speed));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("Performance: could not write the spike log: " + ex.Message);
		}
		if (speed > QuietAboveTimeScale)
		{
			return;
		}
		_spikes++;
		_sinceSummary++;
		_spikeMs += spike.Milliseconds;
		string biggest = UnmeasuredName;
		float biggestMs = spike.UnmeasuredMs;
		Count(UnmeasuredName, spike.UnmeasuredMs);
		for (int i = 0; i < spike.Names.Length; i++)
		{
			Count(spike.Names[i], spike.BucketMs[i]);
			if (spike.BucketMs[i] > biggestMs)
			{
				biggest = spike.Names[i];
				biggestMs = spike.BucketMs[i];
			}
		}
		if (spike.Gcs > 0)
		{
			// A collection's time is not measured on its own: it is in the frame, so the frame counts for it.
			Count(GcName, spike.Milliseconds);
			biggest = GcName;
		}
		Get(biggest).Biggest++;
	}

	private static void Count(string name, float ms)
	{
		if (ms <= 0f)
		{
			return;
		}
		Total total = Get(name);
		total.Spikes++;
		total.Ms += ms;
		if (ms > total.MaxMs)
		{
			total.MaxMs = ms;
		}
	}

	private static Total Get(string name)
	{
		if (!Totals.TryGetValue(name, out Total total))
		{
			total = new Total { Name = name };
			Totals[name] = total;
		}
		return total;
	}

	private void Update()
	{
		float now = Time.realtimeSinceStartup;
		if (now >= _nextFlush)
		{
			_nextFlush = now + FlushSeconds;
			try
			{
				_writer?.Flush();
			}
			catch (Exception)
			{
			}
		}
		if (_nextSummary <= 0f)
		{
			_nextSummary = now + SummaryMinutes * 60f;
		}
		else if (now >= _nextSummary)
		{
			_nextSummary = now + SummaryMinutes * 60f;
			if (_sinceSummary > 0)
			{
				Summarize("every " + SummaryMinutes.ToString("0", CultureInfo.InvariantCulture) + " minutes");
			}
		}
	}

	private void OnApplicationQuit()
	{
		Summarize("game closed");
		try
		{
			_writer?.Flush();
			_writer?.Close();
		}
		catch (Exception)
		{
		}
		_writer = null;
	}

	// The systems by their milliseconds in spikes, biggest first, to the log, the summary file and the JSON file.
	private static void Summarize(string why)
	{
		if (_spikes == 0)
		{
			return;
		}
		_sinceSummary = 0;
		List<Total> totals = new List<Total>(Totals.Values);
		totals.Sort((Total a, Total b) => b.Ms.CompareTo(a.Ms));
		StringBuilder text = new StringBuilder();
		text.Append("Lag spikes since ").Append(_since.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture)).Append(" (").Append(why).Append("): ").Append(_spikes).Append(" spikes, ").Append((_spikeMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" s in all\n");
		text.Append("system | biggest part of | in spikes | ms in spikes | avg ms | max ms\n");
		StringBuilder json = new StringBuilder();
		json.Append("{\"summary\":\"").Append(Escape(why)).Append("\",\"t\":\"").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append("\",\"spikes\":").Append(_spikes).Append(",\"ms\":").Append(Number(_spikeMs)).Append(",\"systems\":[");
		for (int i = 0; i < totals.Count; i++)
		{
			Total total = totals[i];
			double average = (total.Spikes > 0) ? total.Ms / total.Spikes : 0.0;
			text.Append(total.Name).Append(" | ").Append(total.Biggest).Append(" | ").Append(total.Spikes).Append(" | ").Append(total.Ms.ToString("0", CultureInfo.InvariantCulture)).Append(" | ").Append(average.ToString("0.0", CultureInfo.InvariantCulture)).Append(" | ").Append(total.MaxMs.ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
			json.Append((i == 0) ? string.Empty : ",").Append("{\"n\":\"").Append(Escape(total.Name)).Append("\",\"biggest\":").Append(total.Biggest).Append(",\"spikes\":").Append(total.Spikes).Append(",\"ms\":").Append(Number(total.Ms)).Append(",\"max\":").Append(Number(total.MaxMs)).Append('}');
		}
		json.Append("]}");
		int shown = Mathf.Min(totals.Count, 10);
		StringBuilder top = new StringBuilder();
		for (int i = 0; i < shown; i++)
		{
			top.Append((i == 0) ? string.Empty : ", ").Append(totals[i].Name).Append(' ').Append(totals[i].Ms.ToString("0", CultureInfo.InvariantCulture)).Append(" ms (biggest in ").Append(totals[i].Biggest).Append(')');
		}
		Plugin.Log.LogInfo("Performance: " + _spikes + " lag spikes summed up (" + why + "), most time: " + top);
		try
		{
			File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, SummaryName), text.ToString());
			Write(json.ToString());
			_writer?.Flush();
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("Performance: could not write the spike summary: " + ex.Message);
		}
	}

	private static string Line(FrameMonitor.Spike spike, float allocKb, float speed)
	{
		Asset_Map map = GameParam.Map;
		string mode = (GameParam.Gamemode != null) ? GameParam.Gamemode.codename : string.Empty;
		StringBuilder s = new StringBuilder(256);
		s.Append("{\"t\":\"").Append(spike.Clock.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('"');
		s.Append(",\"frame\":").Append(spike.Frame);
		s.Append(",\"ms\":").Append(Number(spike.Milliseconds));
		s.Append(",\"gcs\":").Append(spike.Gcs);
		s.Append(",\"allocKb\":").Append(Number(allocKb));
		s.Append(",\"speed\":").Append(Number(speed));
		s.Append(",\"scene\":\"").Append(Escape(SceneManager.GetActiveScene().name)).Append('"');
		s.Append(",\"map\":\"").Append(Escape((map != null) ? map.codename : string.Empty)).Append('"');
		s.Append(",\"mode\":\"").Append(Escape(mode)).Append('"');
		s.Append(",\"missingScripts\":").Append(spike.MissingScripts);
		s.Append(",\"unmeasuredMs\":").Append(Number(spike.UnmeasuredMs));
		s.Append(",\"measured\":").Append(FrameMonitor.Detailed ? "true" : "false");
		s.Append(",\"top\":[");
		for (int i = 0; i < spike.Names.Length; i++)
		{
			s.Append((i == 0) ? string.Empty : ",").Append("{\"n\":\"").Append(Escape(spike.Names[i])).Append("\",\"ms\":").Append(Number(spike.BucketMs[i])).Append(",\"calls\":").Append(spike.Calls[i]).Append('}');
		}
		s.Append("]}");
		return s.ToString();
	}

	private static void Write(string line)
	{
		if (_writer == null)
		{
			string path = Path.Combine(BepInEx.Paths.BepInExRootPath, FileName);
			if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
			{
				string old = path + ".old";
				if (File.Exists(old))
				{
					File.Delete(old);
				}
				File.Move(path, old);
			}
			_writer = new StreamWriter(path, true, new UTF8Encoding(false));
		}
		_writer.WriteLine(line);
	}

	private static string Number(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	private static string Escape(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
	}
}
