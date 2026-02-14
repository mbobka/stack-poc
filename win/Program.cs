using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

unsafe internal static class Program
{
    // === Настройки PoC ===
    private const ulong StackReserveGiB = 5;
    private const double UseGiB = 4.5;

    private const ulong StackReserveBytes = StackReserveGiB * 1024UL * 1024UL * 1024UL;
    private const ulong UseBytes = (ulong)(UseGiB * 1024d * 1024d * 1024d);

    private const int ChunkBytes = 1 * 1024 * 1024; // 1 MiB
    private const uint STACK_SIZE_PARAM_IS_A_RESERVATION = 0x00010000;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ThreadProc(IntPtr p);

    private static ThreadProc? _proc;

    public static int Main()
    {
        Console.WriteLine($"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}  PID={Environment.ProcessId}");

        if (!Environment.Is64BitProcess)
            throw new InvalidOperationException("Нужен x64 процесс (PlatformTarget=x64).");

        Console.WriteLine($"Stack reserve: {FmtGiB(StackReserveBytes)}");
        Console.WriteLine($"Commit target: {FmtGiB(UseBytes)}");
        Console.WriteLine($"Chunk size   : {ChunkBytes / 1024} KiB");
        Console.WriteLine("--------------------------------------------------");

        _proc = ThreadMain;
        IntPtr start = Marshal.GetFunctionPointerForDelegate(_proc);

        IntPtr hThread = CreateThread(
            IntPtr.Zero,
            (UIntPtr)StackReserveBytes,
            start,
            IntPtr.Zero,
            STACK_SIZE_PARAM_IS_A_RESERVATION,
            out uint tid);

        if (hThread == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateThread failed");

        WaitForSingleObject(hThread, 0xFFFFFFFF);

        if (!GetExitCodeThread(hThread, out uint code))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread failed");

        CloseHandle(hThread);

        Console.WriteLine($"Thread exit code: {code}");
        return (int)code;
    }

    // ===================== BIGSPAN =====================

    public readonly ref struct BigSpan
    {
        private readonly byte* _base;
        public readonly ulong Length;

        public BigSpan(byte* @base, ulong length)
        {
            _base = @base;
            Length = length;
        }

        public ref byte this[ulong index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index >= Length) throw new IndexOutOfRangeException();
                return ref *(_base + (nuint)index);
            }
        }

        public Span<byte> Slice(ulong offset, int length)
        {
            if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((ulong)length > Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            return new Span<byte>(_base + (nuint)offset, length);
        }

        public Span<byte> Slice(ulong offset)
        {
            if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));

            ulong remaining = Length - offset;
            if (remaining > int.MaxValue)
                throw new InvalidOperationException(
                    "Remaining region > 2GB. Specify explicit length <= int.MaxValue.");

            return new Span<byte>(_base + (nuint)offset, (int)remaining);
        }
    }

    // ===================== THREAD =====================

    private static uint ThreadMain(IntPtr _)
    {
        try
        {
            Console.WriteLine("Growing stack...");

            BigSpan big = AllocateOnStack(UseBytes);

            Console.WriteLine($"BigSpan ready: {FmtGiB(big.Length)}");

            // Тест записи
            big[0] = 0x11;
            big[UseBytes / 2] = 0x22;
            big[UseBytes - 1] = 0x33;

            Console.WriteLine("Direct indexing OK");

            // Получаем обычный Span<byte>
            var first100mb = big.Slice(0, 100 * 1024 * 1024);
            first100mb.Fill(0xAA);

            var middle = big.Slice(big.Length / 2, 16 * 1024 * 1024);
            middle[0] = 0x55;

            Console.WriteLine("Slice() OK");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
    }

    // ===================== STACK GROW =====================

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static BigSpan AllocateOnStack(ulong bytes)
    {
        byte* lowest = null;
        Grow(bytes, ref lowest);
        return new BigSpan(lowest, bytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void Grow(ulong remaining, ref byte* lowest)
    {
        int size = (int)Math.Min((ulong)ChunkBytes, remaining);

        byte* p = stackalloc byte[size];
        TouchPages(p, size);

        if ((remaining % (256UL * 1024 * 1024)) < (ulong)ChunkBytes)
            Console.WriteLine($"Remaining: {FmtGiB(remaining)}");

        if (remaining <= (ulong)size)
        {
            lowest = p;
            return;
        }

        Grow(remaining - (ulong)size, ref lowest);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void TouchPages(byte* p, int size)
    {
        const int page = 4096;
        for (int i = 0; i < size; i += page)
            p[i] = 0x5A;

        p[size - 1] = 0xA5;
    }

    // ===================== UTILS =====================

    private static string FmtGiB(ulong bytes)
        => $"{bytes / 1024d / 1024d / 1024d:0.###} GiB";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateThread(
        IntPtr lpThreadAttributes,
        UIntPtr dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}
