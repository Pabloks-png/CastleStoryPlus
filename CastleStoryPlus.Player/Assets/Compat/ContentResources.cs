using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleStoryPlus.Compat
{
	// Resources.Load and Shader.Find for the game's converted content: the player itself has no Resources, the game's
	// Resources folder is in the bundles under the same paths.
	public sealed class ContentResources : ResourcesAPI
	{
		private readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
		private bool shadersLoaded;

		public static void Install()
		{
			ResourcesAPI.overrideAPI = new ContentResources();
		}

		protected override UnityEngine.Object Load(string path, Type systemTypeInstance)
		{
			List<AssetBundle> bundles = GameContent.ResourceBundles(path);
			if (bundles != null)
			{
				foreach (AssetBundle bundle in bundles)
				{
					UnityEngine.Object asset = bundle.LoadAsset(path.ToLowerInvariant(), systemTypeInstance);
					if (asset != null)
					{
						return asset;
					}
				}
			}
			return base.Load(path, systemTypeInstance);
		}

		protected override UnityEngine.Object[] LoadAll(string path, Type systemTypeInstance)
		{
			var result = new List<UnityEngine.Object>();
			foreach (KeyValuePair<string, List<AssetBundle>> pair in GameContent.ResourcesUnder(path))
			{
				foreach (AssetBundle bundle in pair.Value)
				{
					result.AddRange(bundle.LoadAssetWithSubAssets(pair.Key, systemTypeInstance));
				}
			}
			result.AddRange(base.LoadAll(path, systemTypeInstance));
			return result.ToArray();
		}

		protected override Shader FindShaderByName(string name)
		{
			if (!shadersLoaded)
			{
				shadersLoaded = true;
				foreach (AssetBundle bundle in GameContent.Bundles)
				{
					if (bundle.isStreamedSceneAssetBundle)
					{
						continue;
					}
					foreach (Shader shader in bundle.LoadAllAssets<Shader>())
					{
						shaders[shader.name] = shader;
					}
				}
			}
			return shaders.TryGetValue(name, out Shader found) ? found : base.FindShaderByName(name);
		}
	}
}
