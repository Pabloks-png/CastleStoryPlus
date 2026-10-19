using System.Collections.Generic;
using CastleStoryPlus.DevTools.Api;
using CastleStoryPlus.DevTools.Frames;
using UnityEngine;
using static CastleStoryPlus.DevTools.Endpoints.Json;

namespace CastleStoryPlus.DevTools.Endpoints;

// Where each frame's time goes, phase by phase (Frames/FramePhases), over the last seconds.
internal static class FramePhaseEndpoints
{
	internal static void Add(List<Route> routes)
	{
		routes.Add(new Route("GET", "/api/frames/phases", "Each frame split into Unity's phases over the last seconds, average and p95 in ms: fixed (FixedUpdate scripts and physics), update (Update, coroutines, animation, LateUpdate), beforeMainCamera (canvases, cameras drawn first), mainCamera (its culling and drawing, shadows included), afterMainCamera (image effects, overlay interface), present (handing the frame to the graphics card and waiting for it, the frame rate cap, input); and each camera's own time per frame since the last reset", Get)
		{
			Parameters = "seconds (window, default 5), reset (true: the camera times start again from zero)"
		});
	}

	private static object Get(ApiRequest request)
	{
		FramePhases phases = FramePhases.Instance;
		if (phases == null)
		{
			throw new ApiException(409, "Frame phases are not measured");
		}
		float seconds = request.Float("seconds", 5f);
		float since = Time.realtimeSinceStartup - seconds;
		List<FramePhases.Record> records = new List<FramePhases.Record>();
		for (int i = 0; i < phases.Count; i++)
		{
			FramePhases.Record record = phases.Records[(phases.Next - 1 - i + phases.Records.Length) % phases.Records.Length];
			if (record.Time < since)
			{
				break;
			}
			records.Add(record);
		}
		Dictionary<string, object> cameras = new Dictionary<string, object>();
		foreach (KeyValuePair<string, double> camera in phases.CameraMs)
		{
			cameras[camera.Key] = (phases.CameraFrames > 0) ? Round((float)(camera.Value / phases.CameraFrames)) : 0f;
		}
		int cameraFrames = phases.CameraFrames;
		if (request.Bool("reset", false))
		{
			phases.CameraMs.Clear();
			phases.CameraFrames = 0;
		}
		return Obj(
			"frames", records.Count,
			"fps", (records.Count > 0) ? Round(records.Count / seconds) : 0f,
			"total", Stat(records, (FramePhases.Record r) => r.Total),
			"fixed", Stat(records, (FramePhases.Record r) => r.Fixed),
			"update", Stat(records, (FramePhases.Record r) => r.Update),
			"beforeMainCamera", Stat(records, (FramePhases.Record r) => r.BeforeMain),
			"mainCamera", Stat(records, (FramePhases.Record r) => r.Main),
			"afterMainCamera", Stat(records, (FramePhases.Record r) => r.AfterMain),
			"present", Stat(records, (FramePhases.Record r) => r.Present),
			"cameraMsPerFrame", cameras,
			"cameraFrames", cameraFrames);
	}

	private static object Stat(List<FramePhases.Record> records, System.Func<FramePhases.Record, float> value)
	{
		if (records.Count == 0)
		{
			return null;
		}
		List<float> values = new List<float>(records.Count);
		double sum = 0;
		foreach (FramePhases.Record record in records)
		{
			float v = value(record);
			values.Add(v);
			sum += v;
		}
		values.Sort();
		return Obj(
			"avg", Round((float)(sum / values.Count)),
			"p95", Round(values[Mathf.Min(values.Count - 1, (int)(values.Count * 0.95f))]));
	}
}
