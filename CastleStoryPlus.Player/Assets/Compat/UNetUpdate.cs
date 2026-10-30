using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace CastleStoryPlus.Compat
{
	// Unity 5.6's engine ran UNet's message pump (NetworkIdentity.UNetStaticUpdate in UnityEngine.Networking) every
	// frame; Unity 6 knows nothing of it. Without it a host's local client never connects. It runs at the start of
	// Update, before the scripts.
	public static class UNetUpdate
	{
		private struct Pump
		{
		}

		private static Action pump;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Install()
		{
			Type identity = AppDomain.CurrentDomain.GetAssemblies()
				.Where(a => a.GetName().Name == "UnityEngine.Networking")
				.Select(a => a.GetType("UnityEngine.Networking.NetworkIdentity", false))
				.FirstOrDefault(t => t != null);
			MethodInfo update = identity?.GetMethod("UNetStaticUpdate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (update == null)
			{
				return;
			}
			pump = (Action)Delegate.CreateDelegate(typeof(Action), update);
			PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
			for (int i = 0; i < loop.subSystemList.Length; i++)
			{
				if (loop.subSystemList[i].type != typeof(Update))
				{
					continue;
				}
				PlayerLoopSystem[] systems = loop.subSystemList[i].subSystemList;
				var withPump = new PlayerLoopSystem[systems.Length + 1];
				withPump[0] = new PlayerLoopSystem { type = typeof(Pump), updateDelegate = Run };
				Array.Copy(systems, 0, withPump, 1, systems.Length);
				loop.subSystemList[i].subSystemList = withPump;
			}
			PlayerLoop.SetPlayerLoop(loop);
		}

		private static void Run()
		{
			pump();
		}
	}
}
