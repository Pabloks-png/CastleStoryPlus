using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CastleStoryPlus.DevTools.Frames;

// Splits each frame into Unity's phases by time stamps, to see what the frame monitor's "unmeasured" time is:
// - fixed: the fixed steps (FixedUpdate scripts and the physics simulation), from the first FixedUpdate to Update;
// - update: Update scripts, coroutines, animation and LateUpdate scripts, from the first Update to the last LateUpdate;
// - beforeMainCamera: from the last LateUpdate to the main camera starting (the canvases rebuilt, cameras drawn first);
// - mainCamera: the main camera's culling and drawing, shadows included (OnPreCull to OnPostRender);
// - afterMainCamera: its image effects, the overlay interface and the cameras drawn after it, to WaitForEndOfFrame;
// - present: from WaitForEndOfFrame to the next frame's first callback (handing the frame to the graphics card,
//   waiting for it, the frame rate cap, reading input).
// Each camera's own OnPreCull to OnPostRender time is kept too. FramePhasesEarly and FramePhasesLate run first and last
// of all scripts (DefaultExecutionOrder).
internal class FramePhases : MonoBehaviour
{
	internal struct Record
	{
		internal float Time;

		internal float Fixed;

		internal float Update;

		internal float BeforeMain;

		internal float Main;

		internal float AfterMain;

		internal float Present;

		internal float Total;
	}

	internal static FramePhases Instance { get; private set; }

	private const int Size = 2048;

	internal readonly Record[] Records = new Record[Size];

	internal int Count;

	internal int Next;

	// Per camera name, milliseconds over the frames kept in CameraFrames.
	internal readonly Dictionary<string, double> CameraMs = new Dictionary<string, double>();

	internal int CameraFrames;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly Dictionary<Camera, double> _cameraStart = new Dictionary<Camera, double>();

	private double _frameStart = -1;

	private double _fixedStart = -1;

	private double _updateStart = -1;

	private double _lateEnd = -1;

	private double _mainStart = -1;

	private double _mainEnd = -1;

	private double _endOfFrame = -1;

	private int _fixedFrame = -1;

	private int _updateFrame = -1;

	internal double Now => _clock.Elapsed.TotalMilliseconds;

	private void Awake()
	{
		Instance = this;
		gameObject.AddComponent<FramePhasesEarly>();
		gameObject.AddComponent<FramePhasesLate>();
		Camera.onPreCull += OnPreCullAny;
		Camera.onPostRender += OnPostRenderAny;
		StartCoroutine(EndOfFrames());
	}

	private void OnDestroy()
	{
		Camera.onPreCull -= OnPreCullAny;
		Camera.onPostRender -= OnPostRenderAny;
	}

	internal void Reset()
	{
		Count = 0;
		Next = 0;
		CameraMs.Clear();
		CameraFrames = 0;
	}

	// The first callback of a frame closes the frame before it.
	private void StartFrame(double now)
	{
		if (_frameStart >= 0 && _endOfFrame >= _frameStart)
		{
			double first = (_fixedStart >= _frameStart) ? _fixedStart : _updateStart;
			Records[Next] = new Record
			{
				Time = UnityEngine.Time.realtimeSinceStartup,
				Fixed = (_fixedStart >= _frameStart) ? (float)(_updateStart - _fixedStart) : 0f,
				Update = (float)(_lateEnd - _updateStart),
				BeforeMain = (_mainStart >= _lateEnd) ? (float)(_mainStart - _lateEnd) : 0f,
				Main = (_mainEnd >= _mainStart && _mainStart >= _lateEnd) ? (float)(_mainEnd - _mainStart) : 0f,
				AfterMain = (_mainEnd >= _lateEnd) ? (float)(_endOfFrame - _mainEnd) : (float)(_endOfFrame - _lateEnd),
				Present = (float)(now - _endOfFrame),
				Total = (float)(now - _frameStart)
			};
			Next = (Next + 1) % Size;
			if (Count < Size)
			{
				Count++;
			}
			CameraFrames++;
		}
		_frameStart = now;
	}

	internal void OnEarlyFixedUpdate()
	{
		int frame = UnityEngine.Time.frameCount;
		if (frame == _fixedFrame)
		{
			return;
		}
		_fixedFrame = frame;
		double now = Now;
		StartFrame(now);
		_fixedStart = now;
	}

	internal void OnEarlyUpdate()
	{
		int frame = UnityEngine.Time.frameCount;
		if (frame == _updateFrame)
		{
			return;
		}
		_updateFrame = frame;
		double now = Now;
		if (_fixedFrame != frame)
		{
			StartFrame(now);
		}
		_updateStart = now;
	}

	internal void OnLateLateUpdate()
	{
		_lateEnd = Now;
	}

	private void OnPreCullAny(Camera camera)
	{
		double now = Now;
		_cameraStart[camera] = now;
		if (camera == Camera.main)
		{
			_mainStart = now;
		}
	}

	private void OnPostRenderAny(Camera camera)
	{
		double now = Now;
		if (_cameraStart.TryGetValue(camera, out double start))
		{
			string name = camera.name + " (depth " + camera.depth + ")";
			CameraMs.TryGetValue(name, out double ms);
			CameraMs[name] = ms + (now - start);
		}
		if (camera == Camera.main)
		{
			_mainEnd = now;
		}
	}

	private IEnumerator EndOfFrames()
	{
		WaitForEndOfFrame wait = new WaitForEndOfFrame();
		while (true)
		{
			yield return wait;
			_endOfFrame = Now;
		}
	}
}

[DefaultExecutionOrder(-32000)]
internal class FramePhasesEarly : MonoBehaviour
{
	private void FixedUpdate()
	{
		FramePhases.Instance.OnEarlyFixedUpdate();
	}

	private void Update()
	{
		FramePhases.Instance.OnEarlyUpdate();
	}
}

[DefaultExecutionOrder(32000)]
internal class FramePhasesLate : MonoBehaviour
{
	private void LateUpdate()
	{
		FramePhases.Instance.OnLateLateUpdate();
	}
}
