// Captura rapida de un monitor con DXGI Desktop Duplication (GPU), en lugar de GDI.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class DxgiCapture : IDisposable
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, int flags, IntPtr levels,
                                        int nLevels, int sdk, out IntPtr device, out int level, out IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    struct TexDesc { public int Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, MiscFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct Mapped { public IntPtr Data; public int RowPitch, DepthPitch; }

    delegate int EnumFn(IntPtr self, int i, out IntPtr o);
    delegate int DescFn(IntPtr self, IntPtr desc);
    delegate int DuplicateFn(IntPtr self, IntPtr device, out IntPtr dup);
    delegate int AcquireFn(IntPtr self, int timeoutMs, IntPtr info, out IntPtr resource);
    delegate int ReleaseFrameFn(IntPtr self);
    delegate int CreateTexFn(IntPtr self, ref TexDesc desc, IntPtr init, out IntPtr tex);
    delegate void TexDescFn(IntPtr self, out TexDesc desc);
    delegate void CopyFn(IntPtr self, IntPtr dst, IntPtr src);
    delegate int MapFn(IntPtr self, IntPtr res, int sub, int type, int flags, out Mapped m);
    delegate void UnmapFn(IntPtr self, IntPtr res, int sub);

    static T Fn<T>(IntPtr obj, int slot) where T : class
    {
        IntPtr vt = Marshal.ReadIntPtr(obj);
        return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T)) as T;
    }

    static Guid IID_Factory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
    static Guid IID_Output1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
    static Guid IID_Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    const int WaitTimeout = unchecked((int)0x887A0027);

    IntPtr device, context, dup, staging;
    readonly IntPtr frameInfo = Marshal.AllocHGlobal(64);
    int width, height;
    bool hasFrame;

    public static DxgiCapture TryCreate(Rectangle bounds, out string error)
    {
        error = null;
        DxgiCapture c = new DxgiCapture();
        try { c.Init(bounds); return c; }
        catch (Exception ex) { error = ex.Message; c.Dispose(); return null; }
    }

    static void Check(int hr, string what) { if (hr < 0) throw new Exception(what + " (0x" + hr.ToString("X8") + ")"); }

    void Init(Rectangle bounds)
    {
        IntPtr factory;
        Check(CreateDXGIFactory1(ref IID_Factory1, out factory), "CreateDXGIFactory1");
        IntPtr adapter = IntPtr.Zero, output = IntPtr.Zero;
        try
        {
            IntPtr desc = Marshal.AllocHGlobal(128);
            try
            {
                for (int a = 0; output == IntPtr.Zero; a++)
                {
                    IntPtr ad;
                    if (Fn<EnumFn>(factory, 7)(factory, a, out ad) < 0) break; // EnumAdapters
                    for (int o = 0; ; o++)
                    {
                        IntPtr outp;
                        if (Fn<EnumFn>(ad, 7)(ad, o, out outp) < 0) break; // EnumOutputs
                        Fn<DescFn>(outp, 7)(outp, desc); // GetDesc: DeviceName[32] WCHAR, RECT
                        int l = Marshal.ReadInt32(desc, 64), t = Marshal.ReadInt32(desc, 68);
                        int r = Marshal.ReadInt32(desc, 72), b = Marshal.ReadInt32(desc, 76);
                        if (l == bounds.Left && t == bounds.Top && r == bounds.Right && b == bounds.Bottom)
                        {
                            output = outp; adapter = ad;
                            break;
                        }
                        Marshal.Release(outp);
                    }
                    if (output == IntPtr.Zero) Marshal.Release(ad);
                }
            }
            finally { Marshal.FreeHGlobal(desc); }
            if (output == IntPtr.Zero) throw new Exception("no se encontró el monitor en DXGI");

            int level;
            Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context), "D3D11CreateDevice");
            IntPtr out1;
            Check(Marshal.QueryInterface(output, ref IID_Output1, out out1), "IDXGIOutput1");
            try { Check(Fn<DuplicateFn>(out1, 22)(out1, device, out dup), "DuplicateOutput"); }
            finally { Marshal.Release(out1); }
            width = bounds.Width; height = bounds.Height;
        }
        finally
        {
            if (output != IntPtr.Zero) Marshal.Release(output);
            if (adapter != IntPtr.Zero) Marshal.Release(adapter);
            Marshal.Release(factory);
        }
    }

    // Copia la ultima imagen del escritorio en 'target' (32bpp, mismo tamano que el monitor).
    // Devuelve false si aun no hay ninguna imagen. Lanza excepcion si se pierde el acceso (hay que recrear).
    public bool Capture(Bitmap target)
    {
        IntPtr res;
        int hr = Fn<AcquireFn>(dup, 8)(dup, 0, frameInfo, out res);
        if (hr == WaitTimeout) { /* sin cambios: se reutiliza la copia anterior */ }
        else
        {
            Check(hr, "AcquireNextFrame");
            try
            {
                long lastPresent = Marshal.ReadInt64(frameInfo, 0);
                if (lastPresent != 0 || !hasFrame)
                {
                    IntPtr tex;
                    Check(Marshal.QueryInterface(res, ref IID_Texture2D, out tex), "ID3D11Texture2D");
                    try
                    {
                        if (staging == IntPtr.Zero)
                        {
                            TexDesc d;
                            Fn<TexDescFn>(tex, 10)(tex, out d);
                            d.MipLevels = 1; d.ArraySize = 1; d.SampleCount = 1; d.SampleQuality = 0;
                            d.Usage = 3; d.BindFlags = 0; d.CpuAccess = 0x20000; d.MiscFlags = 0; // STAGING, CPU_READ
                            if (d.Width != width || d.Height != height) throw new Exception("tamaño distinto (pantalla girada?)");
                            Check(Fn<CreateTexFn>(device, 5)(device, ref d, IntPtr.Zero, out staging), "CreateTexture2D");
                        }
                        Fn<CopyFn>(context, 47)(context, staging, tex); // CopyResource
                        hasFrame = true;
                    }
                    finally { Marshal.Release(tex); }
                }
            }
            finally
            {
                Marshal.Release(res);
                Fn<ReleaseFrameFn>(dup, 14)(dup);
            }
        }
        if (!hasFrame) return false;

        Mapped m;
        Check(Fn<MapFn>(context, 14)(context, staging, 0, 1, 0, out m), "Map");
        try
        {
            BitmapData bd = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int row = width * 4;
                unsafe
                {
                    for (int y = 0; y < height; y++)
                        Buffer.MemoryCopy((byte*)m.Data + (long)y * m.RowPitch, (byte*)bd.Scan0 + (long)y * bd.Stride, row, row);
                }
            }
            finally { target.UnlockBits(bd); }
        }
        finally { Fn<UnmapFn>(context, 15)(context, staging, 0); }
        return true;
    }

    public void Dispose()
    {
        if (staging != IntPtr.Zero) { Marshal.Release(staging); staging = IntPtr.Zero; }
        if (dup != IntPtr.Zero) { Marshal.Release(dup); dup = IntPtr.Zero; }
        if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
        if (device != IntPtr.Zero) { Marshal.Release(device); device = IntPtr.Zero; }
        Marshal.FreeHGlobal(frameInfo);
    }
}
