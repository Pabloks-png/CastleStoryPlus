using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleStoryPlus.Player.Editor
{
	// Development: -executeMethod CastleStoryPlus.Player.Editor.ShaderProbe.Build writes Build/ShaderProbe/probe.bundle,
	// an uncompressed bundle holding the Assets/Dev probe shaders compiled for the Linux player (OpenGL Core); ...BuildWindows
	// writes Build/ShaderProbe/Windows/probe.bundle for the Windows player (Direct3D 11); ...BuildVulkan writes
	// Build/ShaderProbe/Vulkan/probe.bundle compiled for Vulkan on Linux (the project goes back to OpenGL Core afterwards).
	public static class ShaderProbe
	{
		public static void Build()
		{
			Build(BuildTarget.StandaloneLinux64, Path.Combine("Build", "ShaderProbe"));
		}

		public static void BuildWindows()
		{
			Build(BuildTarget.StandaloneWindows64, Path.Combine("Build", "ShaderProbe", "Windows"));
		}

		public static void BuildVulkan()
		{
			GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneLinux64);
			try
			{
				PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan });
				Build(BuildTarget.StandaloneLinux64, Path.Combine("Build", "ShaderProbe", "Vulkan"));
			}
			finally
			{
				PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, apis);
				AssetDatabase.SaveAssets();
			}
		}

		private static void Build(BuildTarget target, string output)
		{
			Directory.CreateDirectory(output);
			var build = new AssetBundleBuild
			{
				assetBundleName = "probe.bundle",
				assetNames = new[] { "Assets/Dev/ProbeShader.shader", "Assets/Dev/ProbeEmpty.shader", "Assets/Dev/ProbeVariants.shader", "Assets/Dev/ProbeKinds.shader", "Assets/Dev/ProbeChannels.shader", "Assets/Dev/ProbeBuffer.shader" },
			};
			BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.UncompressedAssetBundle, target);
			Debug.Log("[ShaderProbe] " + Path.GetFullPath(Path.Combine(output, "probe.bundle")));
		}
	}
}
