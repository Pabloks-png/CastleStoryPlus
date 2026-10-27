using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace CastleStoryPlus.Bootstrap;

// Counts every managed allocation through Mono's own profiler interface (mono_profiler_install_allocation): objects and
// bytes per type on every thread, and on the main thread a call stack sampled every SampleBytes allocated, counted with
// the bytes it stands for. It answers what fills the heap between two garbage collections, which the frame monitor's
// heap readings around each Update cannot see (rendering callbacks, coroutines, other threads). Read by DevTools at
// /api/allocs.
// Unity's Mono 2.6 needs three workarounds:
// - libmono stops reporting arrays and strings for good (a flag of its own) the first time a method is compiled while
//   no profiler wants allocations; the flag is found in the code after mono_object_new_specific and set again on Start;
// - objects made by methods compiled before Start are never reported (the compiler chose an allocator without the
//   profiler call), so started from the doorstop entry point (environment variable CSP_ALLOC_PROFILER=1) it sees
//   everything, started later only arrays, strings and code compiled since;
// - a native thread allocates its Thread object while it attaches to Mono, before managed code can run on it: the
//   callback goes through a few bytes of native code that leave out threads Mono does not know yet.
// Linux x86-64 only. Costs a native-to-managed call per allocation while it runs.
public static class AllocProfiler
{
	public sealed class TypeRow
	{
		public string Name;

		public long Count;

		public long Bytes;
	}

	public sealed class StackRow
	{
		public string Type;

		public long Samples;

		public long Bytes;

		// Innermost first: the allocating method, then its callers.
		public string[] Frames;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void AllocCallback(IntPtr profiler, IntPtr obj, IntPtr klass);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int FrameCallback(IntPtr method, int nativeOffset, int ilOffset, int managed, IntPtr data);

	public const string EnvVar = "CSP_ALLOC_PROFILER";

	private const string Mono = "libmono.so.0";

	// MONO_PROFILE_ALLOCATIONS.
	private const int ProfileAllocations = 1 << 7;

	private const int RtldNow = 2;

	private const int RtldNoload = 4;

	private const int TypeSlots = 1 << 14;

	private const int StackSlots = 1 << 14;

	// Frames kept per sampled stack, and walked (the profiler's own frames come first and are cut off).
	private const int MaxFrames = 24;

	private const int WalkFrames = MaxFrames + 8;

	public static bool Installed;

	public static bool Running;

	// Started from the doorstop entry point: every allocation is seen, not only arrays, strings and new code.
	public static bool FromStart;

	public static string Error;

	// Bytes allocated on the main thread between two stack samples; 0 samples no stacks.
	public static long SampleBytes = 32 * 1024;

	private static readonly IntPtr[] TypeKeys = new IntPtr[TypeSlots];

	private static readonly long[] TypeCounts = new long[TypeSlots];

	private static readonly long[] TypeBytes = new long[TypeSlots];

	private static readonly IntPtr[] StackClasses = new IntPtr[StackSlots];

	private static readonly int[] StackDepths = new int[StackSlots];

	private static readonly IntPtr[] StackFrames = new IntPtr[StackSlots * MaxFrames];

	private static readonly long[] StackSamples = new long[StackSlots];

	private static readonly long[] StackBytes = new long[StackSlots];

	private static readonly IntPtr[] Walk = new IntPtr[WalkFrames];

	private static readonly Dictionary<IntPtr, string> Names = new Dictionary<IntPtr, string>();

	private static int _walkDepth;

	private static int _stacksUsed;

	private static long _objects;

	private static long _bytes;

	private static long _mainBytes;

	// Bytes of types or stacks that found no room in their table.
	private static long _overflowBytes;

	private static long _untilSample;

	private static long _startedAt;

	private static long _ranTicks;

	// Set on the main thread while it is in the callback or reads the tables: its own allocations are not counted.
	private static bool _busy;

	private static Thread _mainThread;

	private static IntPtr _selfMethod;

	private static IntPtr _profileFlag;

	private static IntPtr _framePointer;

	// Kept alive for as long as Mono may call them.
	private static AllocCallback _allocCallback;

	private static FrameCallback _frameCallback;

	public static long Objects
	{
		get { return Interlocked.Read(ref _objects); }
	}

	public static long Bytes
	{
		get { return Interlocked.Read(ref _bytes); }
	}

	public static long MainThreadBytes
	{
		get { return _mainBytes; }
	}

	public static long OverflowBytes
	{
		get { return Interlocked.Read(ref _overflowBytes); }
	}

	public static double Seconds
	{
		get { return (_ranTicks + (Running ? Stopwatch.GetTimestamp() - _startedAt : 0L)) / (double)Stopwatch.Frequency; }
	}

	// Installs the profiler (once, from the main thread) without starting it.
	public static bool Install()
	{
		if (Installed)
		{
			return true;
		}
		try
		{
			if (IntPtr.Size != 8 || Environment.OSVersion.Platform != PlatformID.Unix)
			{
				Error = "only on Linux x86-64";
				return false;
			}
			IntPtr mono = dlopen(Mono, RtldNow | RtldNoload);
			if (mono == IntPtr.Zero)
			{
				Error = Mono + " is not loaded";
				return false;
			}
			IntPtr threadCurrent = dlsym(mono, "mono_thread_current");
			IntPtr newSpecific = dlsym(mono, "mono_object_new_specific");
			if (threadCurrent == IntPtr.Zero || newSpecific == IntPtr.Zero)
			{
				Error = "mono_thread_current or mono_object_new_specific not found";
				return false;
			}
			_profileFlag = FindProfileFlag(newSpecific);
			if (_profileFlag == IntPtr.Zero)
			{
				Error = "libmono's allocation flag not found (another Mono build?)";
				return false;
			}
			_mainThread = Thread.CurrentThread;
			_selfMethod = typeof(AllocProfiler).GetMethod(nameof(OnAlloc), BindingFlags.Static | BindingFlags.NonPublic).MethodHandle.Value;
			_allocCallback = OnAlloc;
			_frameCallback = OnFrame;
			// Both compile their native-to-managed wrappers here, not inside an allocation.
			IntPtr managed = Marshal.GetFunctionPointerForDelegate(_allocCallback);
			_framePointer = Marshal.GetFunctionPointerForDelegate(_frameCallback);
			IntPtr gate = NativeGate(threadCurrent, managed);
			if (gate == IntPtr.Zero)
			{
				Error = "no executable memory for the native gate";
				return false;
			}
			mono_profiler_install(Marshal.AllocHGlobal(16), IntPtr.Zero);
			mono_profiler_install_allocation(gate);
			Installed = true;
			return true;
		}
		catch (Exception ex)
		{
			Error = ex.GetType().Name + ": " + ex.Message;
			return false;
		}
	}

	// Starts counting (installs first if needed); from the main thread.
	public static bool Start()
	{
		if (!Install())
		{
			return false;
		}
		if (Running)
		{
			return true;
		}
		_untilSample = SampleBytes;
		_startedAt = Stopwatch.GetTimestamp();
		Running = true;
		mono_profiler_set_events(ProfileAllocations);
		Marshal.WriteInt32(_profileFlag, 1);
		// The main thread's first allocation compiles the callback's methods, inside its own guard.
		object[] warm = new object[1];
		GC.KeepAlive(warm);
		return true;
	}

	public static void Stop()
	{
		if (!Running)
		{
			return;
		}
		mono_profiler_set_events(0);
		Running = false;
		_ranTicks += Stopwatch.GetTimestamp() - _startedAt;
	}

	// Forgets everything counted so far; from the main thread.
	public static void Reset()
	{
		_busy = true;
		try
		{
			Array.Clear(TypeKeys, 0, TypeKeys.Length);
			Array.Clear(TypeCounts, 0, TypeCounts.Length);
			Array.Clear(TypeBytes, 0, TypeBytes.Length);
			Array.Clear(StackClasses, 0, StackClasses.Length);
			Array.Clear(StackDepths, 0, StackDepths.Length);
			Array.Clear(StackFrames, 0, StackFrames.Length);
			Array.Clear(StackSamples, 0, StackSamples.Length);
			Array.Clear(StackBytes, 0, StackBytes.Length);
			_stacksUsed = 0;
			Interlocked.Exchange(ref _objects, 0L);
			Interlocked.Exchange(ref _bytes, 0L);
			Interlocked.Exchange(ref _overflowBytes, 0L);
			_mainBytes = 0;
			_untilSample = SampleBytes;
			_ranTicks = 0;
			_startedAt = Stopwatch.GetTimestamp();
		}
		finally
		{
			_busy = false;
		}
	}

	// The types by bytes allocated, the most first; from the main thread.
	public static List<TypeRow> Types()
	{
		List<TypeRow> rows = new List<TypeRow>();
		_busy = true;
		try
		{
			for (int i = 0; i < TypeSlots; i++)
			{
				if (TypeKeys[i] != IntPtr.Zero && TypeCounts[i] > 0)
				{
					rows.Add(new TypeRow { Name = ClassName(TypeKeys[i]), Count = TypeCounts[i], Bytes = TypeBytes[i] });
				}
			}
		}
		finally
		{
			_busy = false;
		}
		rows.Sort((TypeRow a, TypeRow b) => b.Bytes.CompareTo(a.Bytes));
		return rows;
	}

	// The sampled stacks by the bytes they stand for, the most first; from the main thread.
	public static List<StackRow> Stacks()
	{
		List<StackRow> rows = new List<StackRow>();
		_busy = true;
		try
		{
			for (int i = 0; i < StackSlots; i++)
			{
				if (StackSamples[i] == 0)
				{
					continue;
				}
				string[] frames = new string[StackDepths[i]];
				for (int f = 0; f < frames.Length; f++)
				{
					frames[f] = MethodName(StackFrames[i * MaxFrames + f]);
				}
				rows.Add(new StackRow { Type = ClassName(StackClasses[i]), Samples = StackSamples[i], Bytes = StackBytes[i], Frames = frames });
			}
		}
		finally
		{
			_busy = false;
		}
		rows.Sort((StackRow a, StackRow b) => b.Bytes.CompareTo(a.Bytes));
		return rows;
	}

	// Called by Mono (through the native gate) after every allocation while allocations are wanted.
	private static void OnAlloc(IntPtr profiler, IntPtr obj, IntPtr klass)
	{
		if (!Running || obj == IntPtr.Zero)
		{
			return;
		}
		bool main = Thread.CurrentThread == _mainThread;
		if (main)
		{
			if (_busy)
			{
				return;
			}
			_busy = true;
		}
		try
		{
			long size = mono_object_get_size(obj);
			Interlocked.Increment(ref _objects);
			Interlocked.Add(ref _bytes, size);
			CountType(klass, size);
			if (main)
			{
				_mainBytes += size;
				if (SampleBytes > 0)
				{
					_untilSample -= size;
					if (_untilSample <= 0)
					{
						long crossed = 1 + (-_untilSample) / SampleBytes;
						_untilSample += crossed * SampleBytes;
						Sample(klass, crossed * SampleBytes);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			if (main)
			{
				_busy = false;
			}
		}
	}

	private static void CountType(IntPtr klass, long size)
	{
		if (klass == IntPtr.Zero)
		{
			return;
		}
		int slot = Hash(klass) & (TypeSlots - 1);
		for (int i = 0; i < TypeSlots; i++)
		{
			IntPtr key = TypeKeys[slot];
			if (key == IntPtr.Zero)
			{
				key = Interlocked.CompareExchange(ref TypeKeys[slot], klass, IntPtr.Zero);
				if (key == IntPtr.Zero)
				{
					key = klass;
				}
			}
			if (key == klass)
			{
				Interlocked.Increment(ref TypeCounts[slot]);
				Interlocked.Add(ref TypeBytes[slot], size);
				return;
			}
			slot = (slot + 1) & (TypeSlots - 1);
		}
		Interlocked.Add(ref _overflowBytes, size);
	}

	// Main thread only: walks the managed stack and adds it to the table, counted with the bytes it stands for.
	private static void Sample(IntPtr klass, long weight)
	{
		_walkDepth = 0;
		mono_stack_walk_no_il(_framePointer, IntPtr.Zero);
		// The walk starts in the profiler itself: keep what lies beyond OnAlloc.
		int start = 0;
		for (int i = 0; i < _walkDepth; i++)
		{
			if (Walk[i] == _selfMethod)
			{
				start = i + 1;
				break;
			}
		}
		int depth = Math.Min(_walkDepth - start, MaxFrames);
		int hash = Hash(klass);
		for (int i = 0; i < depth; i++)
		{
			hash = hash * 31 + Hash(Walk[start + i]);
		}
		int slot = hash & (StackSlots - 1);
		for (int n = 0; n < StackSlots; n++)
		{
			if (StackSamples[slot] == 0)
			{
				if (_stacksUsed >= StackSlots * 3 / 4)
				{
					break;
				}
				StackClasses[slot] = klass;
				StackDepths[slot] = depth;
				Array.Copy(Walk, start, StackFrames, slot * MaxFrames, depth);
				StackSamples[slot] = 1;
				StackBytes[slot] = weight;
				_stacksUsed++;
				return;
			}
			if (SameStack(slot, klass, start, depth))
			{
				StackSamples[slot]++;
				StackBytes[slot] += weight;
				return;
			}
			slot = (slot + 1) & (StackSlots - 1);
		}
		Interlocked.Add(ref _overflowBytes, weight);
	}

	private static bool SameStack(int slot, IntPtr klass, int start, int depth)
	{
		if (StackClasses[slot] != klass || StackDepths[slot] != depth)
		{
			return false;
		}
		int at = slot * MaxFrames;
		for (int i = 0; i < depth; i++)
		{
			if (StackFrames[at + i] != Walk[start + i])
			{
				return false;
			}
		}
		return true;
	}

	// mono_stack_walk_no_il's callback, once per frame, innermost first; non-zero stops the walk.
	private static int OnFrame(IntPtr method, int nativeOffset, int ilOffset, int managed, IntPtr data)
	{
		if (method == IntPtr.Zero)
		{
			return 0;
		}
		Walk[_walkDepth++] = method;
		return (_walkDepth >= WalkFrames) ? 1 : 0;
	}

	private static int Hash(IntPtr pointer)
	{
		long value = pointer.ToInt64();
		value ^= value >> 29;
		value *= unchecked((long)0xBF58476D1CE4E5B9UL);
		value ^= value >> 32;
		return (int)value & int.MaxValue;
	}

	// The full name of a MonoClass (namespace, nesting and generic arguments).
	private static string ClassName(IntPtr klass)
	{
		if (klass == IntPtr.Zero)
		{
			return "?";
		}
		if (Names.TryGetValue(klass, out string name))
		{
			return name;
		}
		IntPtr text = mono_type_get_name(mono_class_get_type(klass));
		name = (text != IntPtr.Zero) ? Marshal.PtrToStringAnsi(text) : "?";
		if (text != IntPtr.Zero)
		{
			g_free(text);
		}
		Names[klass] = name;
		return name;
	}

	private static string MethodName(IntPtr method)
	{
		if (Names.TryGetValue(method, out string name))
		{
			return name;
		}
		IntPtr text = mono_method_get_name(method);
		name = ClassName(mono_method_get_class(method)) + "." + ((text != IntPtr.Zero) ? Marshal.PtrToStringAnsi(text) : "?");
		Names[method] = name;
		return name;
	}

	// libmono's profile_allocs: the code after mono_object_new_specific (mono_class_get_allocation_ftn) clears it with
	// "test al, 0x80; jnz +n; mov dword [rip + disp32], 0" when allocations are not wanted.
	private static IntPtr FindProfileFlag(IntPtr from)
	{
		for (int i = 0; i < 0x400; i++)
		{
			if (Marshal.ReadByte(from, i) == 0xA8 && Marshal.ReadByte(from, i + 1) == 0x80 && Marshal.ReadByte(from, i + 2) == 0x75 && Marshal.ReadByte(from, i + 4) == 0xC7 && Marshal.ReadByte(from, i + 5) == 0x05 && Marshal.ReadInt32(from, i + 10) == 0)
			{
				int displacement = Marshal.ReadInt32(from, i + 6);
				IntPtr flag = new IntPtr(from.ToInt64() + i + 4 + 10 + displacement);
				int value = Marshal.ReadInt32(flag);
				return (value == 0 || value == 1) ? flag : IntPtr.Zero;
			}
		}
		return IntPtr.Zero;
	}

	// x86-64: calls the managed callback only when mono_thread_current() is set (the thread is attached to Mono).
	//   push rdi; push rsi; push rdx; mov rax, mono_thread_current; call rax; pop rdx; pop rsi; pop rdi
	//   test rax, rax; jz done; mov rax, managed; jmp rax; done: ret
	private static IntPtr NativeGate(IntPtr threadCurrent, IntPtr managed)
	{
		byte[] code = new byte[36]
		{
			0x57, 0x56, 0x52,
			0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,
			0xFF, 0xD0,
			0x5A, 0x5E, 0x5F,
			0x48, 0x85, 0xC0,
			0x74, 0x0C,
			0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,
			0xFF, 0xE0,
			0xC3
		};
		Array.Copy(BitConverter.GetBytes(threadCurrent.ToInt64()), 0, code, 5, 8);
		Array.Copy(BitConverter.GetBytes(managed.ToInt64()), 0, code, 25, 8);
		// PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS.
		IntPtr memory = mmap(IntPtr.Zero, new UIntPtr(4096), 1 | 2 | 4, 0x02 | 0x20, -1, IntPtr.Zero);
		if (memory == IntPtr.Zero || memory.ToInt64() == -1)
		{
			return IntPtr.Zero;
		}
		Marshal.Copy(code, 0, memory, code.Length);
		return memory;
	}

	[DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr dlopen(string file, int mode);

	[DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr dlsym(IntPtr handle, string name);

	[DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr mmap(IntPtr address, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern void mono_profiler_install(IntPtr profiler, IntPtr shutdown);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern void mono_profiler_install_allocation(IntPtr callback);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern void mono_profiler_set_events(int events);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern uint mono_object_get_size(IntPtr obj);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern void mono_stack_walk_no_il(IntPtr callback, IntPtr data);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr mono_class_get_type(IntPtr klass);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr mono_type_get_name(IntPtr type);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr mono_method_get_name(IntPtr method);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr mono_method_get_class(IntPtr method);

	[DllImport(Mono, CallingConvention = CallingConvention.Cdecl)]
	private static extern void g_free(IntPtr pointer);
}
