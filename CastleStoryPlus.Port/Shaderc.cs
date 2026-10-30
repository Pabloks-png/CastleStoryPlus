using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CastleStoryPlus.Port;

// GLSL to SPIR-V with Google's shaderc (Apache-2.0), the native library of the Silk.NET.Shaderc.Native package, which
// ships next to this assembly (runtimes/<rid>/native or the assembly's own folder). It is loaded by path and called
// through function pointers, so that it works the same in the player's Mono and in .NET.
public sealed class Shaderc : IDisposable
{
	public enum Stage
	{
		Vertex = 0,
		Fragment = 1,
	}

	private const int TargetEnvVulkan = 0;
	private const uint VulkanVersion10 = 1u << 22;
	private const int OptimizationPerformance = 2;
	private const int StatusSuccess = 0;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr CreateFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void ReleaseFn(IntPtr handle);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SetTargetEnvFn(IntPtr options, int target, uint version);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SetIntFn(IntPtr options, int value);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr CompileFn(IntPtr compiler, byte[] source, UIntPtr length, int kind, byte[] fileName, byte[] entryPoint, IntPtr options);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int StatusFn(IntPtr result);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate UIntPtr LengthFn(IntPtr result);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr PointerFn(IntPtr result);

	private readonly IntPtr compiler;
	private readonly IntPtr options;
	private readonly ReleaseFn compilerRelease;
	private readonly ReleaseFn optionsRelease;
	private readonly CompileFn compile;
	private readonly StatusFn status;
	private readonly LengthFn resultLength;
	private readonly PointerFn resultBytes;
	private readonly PointerFn errorMessage;
	private readonly ReleaseFn resultRelease;

	public Shaderc()
	{
		IntPtr library = Load(LibraryPath());
		compilerRelease = Function<ReleaseFn>(library, "shaderc_compiler_release");
		optionsRelease = Function<ReleaseFn>(library, "shaderc_compile_options_release");
		compile = Function<CompileFn>(library, "shaderc_compile_into_spv");
		status = Function<StatusFn>(library, "shaderc_result_get_compilation_status");
		resultLength = Function<LengthFn>(library, "shaderc_result_get_length");
		resultBytes = Function<PointerFn>(library, "shaderc_result_get_bytes");
		errorMessage = Function<PointerFn>(library, "shaderc_result_get_error_message");
		resultRelease = Function<ReleaseFn>(library, "shaderc_result_release");
		compiler = Function<CreateFn>(library, "shaderc_compiler_initialize")();
		options = Function<CreateFn>(library, "shaderc_compile_options_initialize")();
		Function<SetTargetEnvFn>(library, "shaderc_compile_options_set_target_env")(options, TargetEnvVulkan, VulkanVersion10);
		Function<SetIntFn>(library, "shaderc_compile_options_set_optimization_level")(options, OptimizationPerformance);
		if (compiler == IntPtr.Zero || options == IntPtr.Zero)
		{
			throw new InvalidOperationException("shaderc did not start");
		}
	}

	// The SPIR-V of a GLSL stage; throws with the compiler's messages when it fails.
	public byte[] Compile(string source, Stage stage, string name)
	{
		byte[] text = Encoding.UTF8.GetBytes(source);
		IntPtr result = compile(compiler, text, (UIntPtr)text.Length, (int)stage, Zero(name), Zero("main"), options);
		try
		{
			if (status(result) != StatusSuccess)
			{
				throw new InvalidDataException(Marshal.PtrToStringAnsi(errorMessage(result)));
			}
			var spirv = new byte[(int)resultLength(result)];
			Marshal.Copy(resultBytes(result), spirv, 0, spirv.Length);
			return spirv;
		}
		finally
		{
			resultRelease(result);
		}
	}

	public void Dispose()
	{
		optionsRelease(options);
		compilerRelease(compiler);
	}

	private static byte[] Zero(string value)
	{
		return Encoding.UTF8.GetBytes(value + "\0");
	}

	private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	private static string LibraryPath()
	{
		string name = Windows ? "shaderc_shared.dll" : "libshaderc_shared.so";
		string rid = Windows ? "win-x64" : "linux-x64";
		string dir = Path.GetDirectoryName(typeof(Shaderc).Assembly.Location);
		foreach (string path in new[] { Path.Combine(dir, "runtimes", rid, "native", name), Path.Combine(dir, name) })
		{
			if (File.Exists(path))
			{
				return path;
			}
		}
		throw new FileNotFoundException("shaderc not found next to " + dir, name);
	}

	private static T Function<T>(IntPtr library, string name) where T : Delegate
	{
		IntPtr pointer = Windows ? GetProcAddress(library, name) : dlsym(library, name);
		if (pointer == IntPtr.Zero)
		{
			throw new EntryPointNotFoundException(name);
		}
		return (T)Marshal.GetDelegateForFunctionPointer(pointer, typeof(T));
	}

	private static IntPtr Load(string path)
	{
		IntPtr handle = Windows ? LoadLibraryW(path) : dlopen(path, RtldNow);
		if (handle == IntPtr.Zero)
		{
			throw new DllNotFoundException(path + (Windows ? "" : ": " + Marshal.PtrToStringAnsi(dlerror())));
		}
		return handle;
	}

	private const int RtldNow = 2;

	[DllImport("libdl.so.2")]
	private static extern IntPtr dlopen(string path, int flags);

	[DllImport("libdl.so.2")]
	private static extern IntPtr dlsym(IntPtr handle, string name);

	[DllImport("libdl.so.2")]
	private static extern IntPtr dlerror();

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryW(string path);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
