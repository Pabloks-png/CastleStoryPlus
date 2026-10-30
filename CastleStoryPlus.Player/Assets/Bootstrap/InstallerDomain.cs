using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CastleStoryPlus.Player
{
	// Runs CastleStoryPlus.Port's installer in an AppDomain of its own, based in Installer/, where its libraries (Cecil
	// 0.11, AssetsTools.NET) are found first. In the player's own domain BepInEx has already loaded its older Cecil, which
	// the installer would bind to, and then fail to find the rest. Nothing of Unity is used on the installer's side.
	public static class InstallerDomain
	{
		private const string InstallerAssembly = "CastleStoryPlus.Port.dll";
		private const string InstallerType = "CastleStoryPlus.Port.Installer";

		public static bool Exists(string installerDir)
		{
			return File.Exists(Path.Combine(installerDir, InstallerAssembly));
		}

		// Runs the install; problems is the game files that are missing or differ (empty when another error stopped it).
		// Returns null when it is done, else what stopped it.
		public static string Run(string installerDir, string playerAssembly, string gameData, string playerData, string content, Sink sink)
		{
			AppDomain domain = AppDomain.CreateDomain("CastleStoryPlus installer", null, new AppDomainSetup { ApplicationBase = installerDir });
			try
			{
				var host = (Host)domain.CreateInstanceFromAndUnwrap(playerAssembly, typeof(Host).FullName);
				return host.Run(installerDir, gameData, playerData, content, sink);
			}
			finally
			{
				AppDomain.Unload(domain);
			}
		}

		// The player's side: what the installer reports, called from its domain.
		public sealed class Sink : MarshalByRefObject
		{
			private readonly Action<string> log;
			private readonly Action<string, float> progress;
			private readonly Action<string[]> problems;

			public Sink(Action<string> log, Action<string, float> progress, Action<string[]> problems)
			{
				this.log = log;
				this.progress = progress;
				this.problems = problems;
			}

			public void Log(string line)
			{
				log(line);
			}

			public void Progress(string step, float done)
			{
				progress(step, done);
			}

			public void Problems(string[] files)
			{
				problems(files);
			}

			// Lives as long as the install, however long that takes.
			public override object InitializeLifetimeService()
			{
				return null;
			}
		}

		// The installer's side. Only strings cross back: its exceptions are not serializable.
		public sealed class Host : MarshalByRefObject
		{
			public string Run(string installerDir, string gameData, string playerData, string content, Sink sink)
			{
				try
				{
					Type type = Assembly.LoadFrom(Path.Combine(installerDir, InstallerAssembly)).GetType(InstallerType, true);
					object installer = Activator.CreateInstance(type);
					type.GetField("GameDataDir").SetValue(installer, gameData);
					type.GetField("PlayerDataDir").SetValue(installer, playerData);
					type.GetField("ContentDir").SetValue(installer, content);
					type.GetField("ClassPackage").SetValue(installer, Path.Combine(installerDir, "classdata.tpk"));
					type.GetField("Manifest").SetValue(installer, Path.Combine(installerDir, "game-files.sha256"));
					type.GetField("Log").SetValue(installer, new Action<string>(sink.Log));
					type.GetField("Progress").SetValue(installer, new Action<string, float>(sink.Progress));
					type.GetMethod("Run").Invoke(installer, null);
					return null;
				}
				catch (Exception e)
				{
					Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
					// GameFilesException: the files that are missing or differ.
					if (cause.GetType().GetProperty("Problems")?.GetValue(cause) is List<string> files)
					{
						sink.Problems(files.ToArray());
					}
					return cause.ToString();
				}
			}

			public override object InitializeLifetimeService()
			{
				return null;
			}
		}
	}
}
