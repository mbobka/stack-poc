using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

unsafe class Program
{
    const ulong StackSize = 5UL * 1024 * 1024 * 1024; // 5 GiB
    const ulong UseBytes  = (ulong)(4.5 * 1024 * 1024 * 1024);

    const int PROT_READ  = 1;
    const int PROT_WRITE = 2;

    const int MAP_PRIVATE   = 2;
    const int MAP_ANONYMOUS = 0x20;

    static void* _stackBase;
    static ulong _stackSize;

    delegate IntPtr ThreadStart(IntPtr arg);
    static ThreadStart? _start;

    static int Main()
    {
        Console.WriteLine($"PID {Environment.ProcessId}");

        void* stack = mmap(null, (nuint)StackSize,
            PROT_READ | PROT_WRITE,
            MAP_PRIVATE | MAP_ANONYMOUS,
            -1, 0);

        if (stack == (void*)(-1))
            throw new Exception("mmap failed");

        _stackBase = stack;
        _stackSize = StackSize;

        pthread_attr_t attr;
        pthread_attr_init(out attr);

        if (pthread_attr_setstack(ref attr, stack, (nuint)StackSize) != 0)
            throw new Exception("pthread_attr_setstack failed");

        _start = ThreadMain;
        IntPtr fn = Marshal.GetFunctionPointerForDelegate(_start);

        ulong thread;
        if (pthread_create(out thread, ref attr, fn, IntPtr.Zero) != 0)
            throw new Exception("pthread_create failed");

        pthread_join(thread, out _);

        Console.WriteLine("Done");
        return 0;
    }

    static IntPtr ThreadMain(IntPtr arg)
    {
        Console.WriteLine("Thread running on custom 5GB stack");

        // Используем нижнюю часть mmap региона как arena
        byte* arenaBase = (byte*)_stackBase;

        BigSpan big = new BigSpan(arenaBase, UseBytes);

        // тест записи
        big[0] = 1;
        big[UseBytes / 2] = 2;
        big[UseBytes - 1] = 3;

        // тест Slice()
        var middle = big.Slice(UseBytes / 2, 1024 * 1024);
        middle.Fill(0xAA);

        Console.WriteLine("BigSpan OK");
        return IntPtr.Zero;
    }

    // ===================== BigSpan =====================

    public readonly ref struct BigSpan
    {
        readonly byte* _base;
        public readonly ulong Length;

        public BigSpan(byte* b, ulong len)
        {
            _base = b;
            Length = len;
        }

        public ref byte this[ulong index]
        {
            get
            {
                if (index >= Length) throw new IndexOutOfRangeException();
                return ref *(_base + (nuint)index);
            }
        }

        public Span<byte> Slice(ulong offset, int length)
        {
            if ((ulong)length > Length - offset)
                throw new ArgumentOutOfRangeException();

            return new Span<byte>(_base + (nuint)offset, length);
        }
    }

    // ===================== libc =====================

    [StructLayout(LayoutKind.Sequential)]
    struct pthread_attr_t
    {
        private fixed byte data[64]; // достаточно для musl
    }

    [DllImport("libc")]
    static extern void* mmap(
        void* addr,
        nuint length,
        int prot,
        int flags,
        int fd,
        long offset);

    [DllImport("libc")]
    static extern int pthread_attr_init(out pthread_attr_t attr);

    [DllImport("libc")]
    static extern int pthread_attr_setstack(
        ref pthread_attr_t attr,
        void* stackaddr,
        nuint stacksize);

    [DllImport("libc")]
    static extern int pthread_create(
        out ulong thread,
        ref pthread_attr_t attr,
        IntPtr start_routine,
        IntPtr arg);

    [DllImport("libc")]
    static extern int pthread_join(
        ulong thread,
        out IntPtr retval);
}
