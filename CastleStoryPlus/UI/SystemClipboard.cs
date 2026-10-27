using System;
using System.Diagnostics;
using UnityEngine;

namespace CastleStoryPlus.UI;

// The system clipboard. The game's Unity reads and writes only its own copy on Linux (GUIUtility.systemCopyBuffer
// gives back what the game itself copied, not what was copied in another program), so on Linux the clipboard is
// read and written with xclip, else wl-paste / wl-copy or xsel, whichever is installed; Unity's copy is kept too.
internal static class SystemClipboard
{
	private static bool IsLinux => Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;

	internal static string Get()
	{
		if (IsLinux)
		{
			string text = Run("xclip", "-o -selection clipboard", null) ?? Run("wl-paste", "-n", null) ?? Run("xsel", "-bo", null);
			if (text != null)
			{
				return text;
			}
		}
		return GUIUtility.systemCopyBuffer;
	}

	internal static void Set(string text)
	{
		GUIUtility.systemCopyBuffer = text;
		if (IsLinux && Run("xclip", "-i -selection clipboard", text) == null && Run("wl-copy", string.Empty, text) == null)
		{
			Run("xsel", "-bi", text);
		}
	}

	// Runs the tool (stdin: input, when given); its output, or null when it is not there or fails.
	private static string Run(string tool, string arguments, string input)
	{
		try
		{
			ProcessStartInfo info = new ProcessStartInfo(tool, arguments)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = input != null,
				RedirectStandardOutput = input == null,
				RedirectStandardError = false
			};
			using (Process process = Process.Start(info))
			{
				if (input != null)
				{
					process.StandardInput.Write(input);
					process.StandardInput.Close();
					// xclip stays behind to hand the text out; it is not waited for.
					return string.Empty;
				}
				string output = process.StandardOutput.ReadToEnd();
				process.WaitForExit(2000);
				return (process.HasExited && process.ExitCode == 0) ? output : null;
			}
		}
		catch (Exception)
		{
			return null;
		}
	}
}
