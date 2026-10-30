using System.IO;
using UnityEditor;
using UnityEngine;

namespace CastleStoryPlus.Player.Editor
{
	// Development: -executeMethod CastleStoryPlus.Player.Editor.AnimationProbe.Build writes
	// Build/AnimationProbe/probe.bundle, an uncompressed bundle with animation clips made here (a constant Euler rotation
	// like the game's sky orbit clips, a quaternion rotation, a light colour), to see how Unity 6 binds their curves.
	public static class AnimationProbe
	{
		private const string Folder = "Assets/Dev";

		public static void Build()
		{
			string euler = Clip("ProbeEuler", clip =>
			{
				clip.SetCurve("Cardinal/Elevation", typeof(Transform), "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, 0f, 45f));
				clip.SetCurve("Cardinal/Elevation", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, 0f, 0f));
				clip.SetCurve("Cardinal/Elevation", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, 0f, 0f));
			});
			string quaternion = Clip("ProbeQuaternion", clip =>
			{
				clip.SetCurve("Cardinal", typeof(Transform), "localRotation.x", AnimationCurve.Constant(0f, 0f, 0.38f));
				clip.SetCurve("Cardinal", typeof(Transform), "localRotation.y", AnimationCurve.Constant(0f, 0f, 0f));
				clip.SetCurve("Cardinal", typeof(Transform), "localRotation.z", AnimationCurve.Constant(0f, 0f, 0f));
				clip.SetCurve("Cardinal", typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, 0f, 0.92f));
			});
			string light = Clip("ProbeLight", clip =>
			{
				clip.SetCurve("", typeof(Light), "m_Color.r", AnimationCurve.Constant(0f, 0f, 0.5f));
			});
			string output = Path.Combine("Build", "AnimationProbe");
			Directory.CreateDirectory(output);
			var build = new AssetBundleBuild { assetBundleName = "probe.bundle", assetNames = new[] { euler, quaternion, light } };
			BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneLinux64);
			Debug.Log("[AnimationProbe] " + Path.GetFullPath(Path.Combine(output, "probe.bundle")));
		}

		private static string Clip(string name, System.Action<AnimationClip> fill)
		{
			string path = Folder + "/" + name + ".anim";
			var clip = new AnimationClip { name = name };
			fill(clip);
			AssetDatabase.CreateAsset(clip, path);
			AssetDatabase.SaveAssets();
			return path;
		}
	}
}
