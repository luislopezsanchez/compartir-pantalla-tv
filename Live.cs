// Transmision en vivo H.264 + AAC en MP4 fragmentado (reproducido con Media Source Extensions en la TV).
// Usa los codificadores de Media Foundation incluidos en Windows: no requiere instalar nada.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#region Interop Media Foundation

[StructLayout(LayoutKind.Sequential)]
struct MFT_INPUT_STREAM_INFO { public long hnsMaxLatency; public int dwFlags, cbSize, cbMaxLookahead, cbAlignment; }

[StructLayout(LayoutKind.Sequential)]
struct MFT_OUTPUT_STREAM_INFO { public int dwFlags, cbSize, cbAlignment; }

[StructLayout(LayoutKind.Sequential)]
struct MFT_OUTPUT_DATA_BUFFER { public int dwStreamID; public IntPtr pSample; public int dwStatus; public IntPtr pEvents; }

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare();
    [PreserveSig] int GetUINT32(ref Guid k, out int v);
    [PreserveSig] int GetUINT64(ref Guid k, out long v);
    void GetDouble();
    [PreserveSig] int GetGUID(ref Guid k, out Guid v);
    void GetStringLength(); void GetString(); void GetAllocatedString();
    [PreserveSig] int GetBlobSize(ref Guid k, out int size);
    [PreserveSig] int GetBlob(ref Guid k, [Out] byte[] buf, int size, out int got);
    void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
    [PreserveSig] int SetUINT32(ref Guid k, int v);
    [PreserveSig] int SetUINT64(ref Guid k, long v);
    void SetDouble();
    [PreserveSig] int SetGUID(ref Guid k, ref Guid v);
    void SetString();
    [PreserveSig] int SetBlob(ref Guid k, [In] byte[] buf, int size);
    void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample
{
    void GetItem(); void GetItemType(); void CompareItem(); void Compare();
    void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
    void GetStringLength(); void GetString(); void GetAllocatedString();
    void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems();
    void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString(); void SetBlob(); void SetUnknown();
    void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
    [PreserveSig] int GetSampleFlags(out int f);
    [PreserveSig] int SetSampleFlags(int f);
    [PreserveSig] int GetSampleTime(out long t);
    [PreserveSig] int SetSampleTime(long t);
    [PreserveSig] int GetSampleDuration(out long d);
    [PreserveSig] int SetSampleDuration(long d);
    [PreserveSig] int GetBufferCount(out int n);
    [PreserveSig] int GetBufferByIndex(int i, out IMFMediaBuffer b);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer b);
    [PreserveSig] int AddBuffer(IMFMediaBuffer b);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr p, out int max, out int cur);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out int n);
    [PreserveSig] int SetCurrentLength(int n);
    [PreserveSig] int GetMaxLength(out int n);
}

[ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFTransform
{
    [PreserveSig] int GetStreamLimits(out int a, out int b, out int c, out int d);
    [PreserveSig] int GetStreamCount(out int i, out int o);
    [PreserveSig] int GetStreamIDs(int a, IntPtr b, int c, IntPtr d);
    [PreserveSig] int GetInputStreamInfo(int id, out MFT_INPUT_STREAM_INFO info);
    [PreserveSig] int GetOutputStreamInfo(int id, out MFT_OUTPUT_STREAM_INFO info);
    [PreserveSig] int GetAttributes(out IntPtr a);
    [PreserveSig] int GetInputStreamAttributes(int id, out IntPtr a);
    [PreserveSig] int GetOutputStreamAttributes(int id, out IntPtr a);
    [PreserveSig] int DeleteInputStream(int id);
    [PreserveSig] int AddInputStreams(int n, IntPtr ids);
    [PreserveSig] int GetInputAvailableType(int id, int idx, out IMFMediaType t);
    [PreserveSig] int GetOutputAvailableType(int id, int idx, out IMFMediaType t);
    [PreserveSig] int SetInputType(int id, IMFMediaType t, int flags);
    [PreserveSig] int SetOutputType(int id, IMFMediaType t, int flags);
    [PreserveSig] int GetInputCurrentType(int id, out IMFMediaType t);
    [PreserveSig] int GetOutputCurrentType(int id, out IMFMediaType t);
    [PreserveSig] int GetInputStatus(int id, out int f);
    [PreserveSig] int GetOutputStatus(out int f);
    [PreserveSig] int SetOutputBounds(long a, long b);
    [PreserveSig] int ProcessEvent(int id, IntPtr e);
    [PreserveSig] int ProcessMessage(int msg, IntPtr param);
    [PreserveSig] int ProcessInput(int id, IMFSample s, int flags);
    [PreserveSig] int ProcessOutput(int flags, int count, ref MFT_OUTPUT_DATA_BUFFER buf, out int status);
}

[ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICodecAPI
{
    [PreserveSig] int IsSupported(ref Guid api);
    [PreserveSig] int IsModifiable(ref Guid api);
    [PreserveSig] int GetParameterRange(ref Guid api, IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int GetParameterValues(ref Guid api, IntPtr a, IntPtr b);
    [PreserveSig] int GetDefaultValue(ref Guid api, IntPtr v);
    [PreserveSig] int GetValue(ref Guid api, IntPtr v);
    [PreserveSig] int SetValue(ref Guid api, [In, MarshalAs(UnmanagedType.Struct)] ref object v);
}

static class MF
{
    [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFStartup(int ver, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFCreateMediaType(out IMFMediaType t);
    [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFCreateSample(out IMFSample s);
    [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer b);

    public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid AvgBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid Profile = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid YuvMatrix = new Guid("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid NominalRange = new Guid("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid AChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid ARate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid ABytesPerSec = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid ABlockAlign = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid ABits = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid AacPayload = new Guid("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    public static readonly Guid AacProfile = new Guid("7632f0e6-9538-4d61-acda-ea29c8c14456");

    public const int NeedMoreInput = unchecked((int)0xC00D6D72);
    public const int StreamChange = unchecked((int)0xC00D6D61);
    public const int NotAccepting = unchecked((int)0xC00D36B5);

    static bool started;
    public static void Startup()
    {
        lock (typeof(MF))
        {
            if (started) return;
            Check(MFStartup(0x00020070, 0), "MFStartup");
            started = true;
        }
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new Exception(what + " fallÃ³ (0x" + hr.ToString("X8") + ")");
    }

    public static object Create(string clsid)
    {
        return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid)));
    }

    public static IMFMediaType NewType(Guid major, Guid sub)
    {
        IMFMediaType t;
        Check(MFCreateMediaType(out t), "MFCreateMediaType");
        Guid k = MajorType, v = major;
        Check(t.SetGUID(ref k, ref v), "MajorType");
        k = SubType; v = sub;
        Check(t.SetGUID(ref k, ref v), "SubType");
        return t;
    }

    public static void Set(IMFMediaType t, Guid k, int v) { Check(t.SetUINT32(ref k, v), "SetUINT32 " + k); }
    public static void Set(IMFMediaType t, Guid k, int hi, int lo) { Check(t.SetUINT64(ref k, ((long)hi << 32) | (uint)lo), "SetUINT64 " + k); }

    public static IMFSample NewSample(int size, out IMFMediaBuffer buf)
    {
        IMFSample s;
        Check(MFCreateSample(out s), "MFCreateSample");
        Check(MFCreateMemoryBuffer(Math.Max(size, 1), out buf), "MFCreateMemoryBuffer");
        Check(s.AddBuffer(buf), "AddBuffer");
        return s;
    }

    public static IMFSample SampleFrom(byte[] data, int len, long time, long dur)
    {
        IMFMediaBuffer buf;
        IMFSample s = NewSample(len, out buf);
        IntPtr p; int max, cur;
        Check(buf.Lock(out p, out max, out cur), "Lock");
        Marshal.Copy(data, 0, p, len);
        buf.Unlock();
        buf.SetCurrentLength(len);
        Marshal.ReleaseComObject(buf);
        s.SetSampleTime(time);
        s.SetSampleDuration(dur);
        return s;
    }

    public static byte[] Bytes(IMFSample s)
    {
        IMFMediaBuffer buf;
        Check(s.ConvertToContiguousBuffer(out buf), "ConvertToContiguousBuffer");
        try
        {
            IntPtr p; int max, cur;
            Check(buf.Lock(out p, out max, out cur), "Lock");
            byte[] r = new byte[cur];
            Marshal.Copy(p, r, 0, cur);
            buf.Unlock();
            return r;
        }
        finally { Marshal.ReleaseComObject(buf); }
    }

    public static void Release(object o)
    {
        if (o != null && Marshal.IsComObject(o)) { try { Marshal.ReleaseComObject(o); } catch { } }
    }
}

// Envuelve un MFT sincrono (codificador): entrada de muestras y recogida de salidas.
class Mft : IDisposable
{
    public readonly IMFTransform T;
    readonly object obj;
    bool provides;
    int outSize;

    public Mft(object o) { obj = o; T = (IMFTransform)o; }

    public void Begin(int minOutSize)
    {
        MFT_OUTPUT_STREAM_INFO info;
        MF.Check(T.GetOutputStreamInfo(0, out info), "GetOutputStreamInfo");
        provides = (info.dwFlags & 0x300) != 0;
        outSize = Math.Max(info.cbSize, minOutSize);
        T.ProcessMessage(0x10000000, IntPtr.Zero); // NOTIFY_BEGIN_STREAMING
        T.ProcessMessage(0x10000003, IntPtr.Zero); // NOTIFY_START_OF_STREAM
    }

    public List<KeyValuePair<long, byte[]>> Process(IMFSample input)
    {
        List<KeyValuePair<long, byte[]>> res = new List<KeyValuePair<long, byte[]>>();
        int hr = T.ProcessInput(0, input, 0);
        if (hr == MF.NotAccepting)
        {
            Drain(res);
            hr = T.ProcessInput(0, input, 0);
        }
        MF.Check(hr, "ProcessInput");
        Drain(res);
        return res;
    }

    void Drain(List<KeyValuePair<long, byte[]>> res)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            MFT_OUTPUT_DATA_BUFFER[] arr = new MFT_OUTPUT_DATA_BUFFER[1];
            IMFSample mine = null;
            if (!provides)
            {
                IMFMediaBuffer b;
                mine = MF.NewSample(outSize, out b);
                Marshal.ReleaseComObject(b);
                arr[0].pSample = Marshal.GetComInterfaceForObject(mine, typeof(IMFSample));
            }
            int status;
            int hr = T.ProcessOutput(0, 1, ref arr[0], out status);
            if (arr[0].pEvents != IntPtr.Zero) Marshal.Release(arr[0].pEvents);
            IMFSample got = null;
            try
            {
                if (hr == MF.NeedMoreInput) return;
                if (hr == MF.StreamChange)
                {
                    IMFMediaType t;
                    MF.Check(T.GetOutputAvailableType(0, 0, out t), "GetOutputAvailableType");
                    MF.Check(T.SetOutputType(0, t, 0), "SetOutputType (cambio)");
                    MF.Release(t);
                    continue;
                }
                MF.Check(hr, "ProcessOutput");
                if (mine != null) got = mine;
                else if (arr[0].pSample != IntPtr.Zero) got = (IMFSample)Marshal.GetObjectForIUnknown(arr[0].pSample);
                if (got == null) continue;
                long t0;
                got.GetSampleTime(out t0);
                res.Add(new KeyValuePair<long, byte[]>(t0, MF.Bytes(got)));
            }
            finally
            {
                if (arr[0].pSample != IntPtr.Zero) Marshal.Release(arr[0].pSample);
                if (got != null && got != mine) MF.Release(got);
                MF.Release(mine);
            }
        }
    }

    public void Dispose() { MF.Release(obj); }
}

class H264Encoder : IDisposable
{
    static readonly Guid LowLatency = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    static readonly Guid GopSize = new Guid("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    static readonly Guid MeanBitrate = new Guid("f7222374-2144-4815-b550-a37f8e12ee52");
    static readonly Guid BFrames = new Guid("8d390aac-dc5c-4200-b57f-814d04babab2");
    static readonly Guid ForceKeyFrame = new Guid("398c1b98-8353-475a-9ef2-8f265d260345");

    readonly Mft mft;
    readonly ICodecAPI api;
    readonly int size;
    public string Profile;

    public H264Encoder(int w, int h, int fps, int bitrate)
    {
        MF.Startup();
        object o = MF.Create("6ca50344-051a-4ded-9779-a43305165e35"); // CLSID_MSH264EncoderMFT
        mft = new Mft(o);
        api = o as ICodecAPI;
        if (api != null)
        {
            SetApi(LowLatency, true);
            SetApi(BFrames, (uint)0);
            SetApi(GopSize, (uint)(fps * 2));
            SetApi(MeanBitrate, (uint)bitrate);
        }
        Exception last = null;
        foreach (int prof in new int[] { 100, 77, 66 }) // High, Main, Baseline
        {
            IMFMediaType t = MF.NewType(MF.Video, MF.H264);
            MF.Set(t, MF.AvgBitrate, bitrate);
            MF.Set(t, MF.FrameSize, w, h);
            MF.Set(t, MF.FrameRate, fps, 1);
            MF.Set(t, MF.PixelAspect, 1, 1);
            MF.Set(t, MF.Interlace, 2);
            MF.Set(t, MF.Profile, prof);
            MF.Set(t, MF.YuvMatrix, 1);     // BT.709
            MF.Set(t, MF.NominalRange, 2);  // 16-235
            int hr = mft.T.SetOutputType(0, t, 0);
            MF.Release(t);
            if (hr >= 0) { Profile = prof == 100 ? "High" : prof == 77 ? "Main" : "Baseline"; last = null; break; }
            last = new Exception("SetOutputType H.264 (0x" + hr.ToString("X8") + ")");
        }
        if (last != null) throw last;
        IMFMediaType it = MF.NewType(MF.Video, MF.NV12);
        MF.Set(it, MF.FrameSize, w, h);
        MF.Set(it, MF.FrameRate, fps, 1);
        MF.Set(it, MF.PixelAspect, 1, 1);
        MF.Set(it, MF.Interlace, 2);
        MF.Set(it, MF.YuvMatrix, 1);
        MF.Set(it, MF.NominalRange, 2);
        MF.Check(mft.T.SetInputType(0, it, 0), "SetInputType NV12");
        MF.Release(it);
        size = w * h * 3 / 2;
        mft.Begin(size);
    }

    void SetApi(Guid k, object v)
    {
        try { api.SetValue(ref k, ref v); } catch { }
    }

    public void ForceKey() { if (api != null) SetApi(ForceKeyFrame, (uint)1); }

    public List<KeyValuePair<long, byte[]>> Encode(byte[] nv12, long time, long dur)
    {
        IMFSample s = MF.SampleFrom(nv12, size, time, dur);
        try { return mft.Process(s); }
        finally { MF.Release(s); }
    }

    public void Dispose() { mft.Dispose(); }
}

class AacEncoder : IDisposable
{
    readonly Mft mft;

    public AacEncoder(int rate, int bytesPerSec)
    {
        MF.Startup();
        mft = new Mft(MF.Create("93AF0C51-2275-45d2-A35B-F2BA21CAED00")); // CLSID_AACMFTEncoder
        IMFMediaType it = MF.NewType(MF.Audio, MF.PCM);
        MF.Set(it, MF.ABits, 16);
        MF.Set(it, MF.ARate, rate);
        MF.Set(it, MF.AChannels, 2);
        MF.Set(it, MF.ABlockAlign, 4);
        MF.Set(it, MF.ABytesPerSec, rate * 4);
        IMFMediaType ot = MF.NewType(MF.Audio, MF.AAC);
        MF.Set(ot, MF.ABits, 16);
        MF.Set(ot, MF.ARate, rate);
        MF.Set(ot, MF.AChannels, 2);
        MF.Set(ot, MF.ABytesPerSec, bytesPerSec);
        MF.Set(ot, MF.AacPayload, 0);       // AAC crudo (sin ADTS)
        MF.Set(ot, MF.AacProfile, 0x29);
        int hr = mft.T.SetInputType(0, it, 0);
        if (hr >= 0) hr = mft.T.SetOutputType(0, ot, 0);
        else
        {
            MF.Check(mft.T.SetOutputType(0, ot, 0), "SetOutputType AAC");
            hr = mft.T.SetInputType(0, it, 0);
        }
        MF.Check(hr, "Tipos de audio AAC");
        MF.Release(it);
        MF.Release(ot);
        mft.Begin(8192);
    }

    public List<KeyValuePair<long, byte[]>> Encode(byte[] pcm, long time, long dur)
    {
        IMFSample s = MF.SampleFrom(pcm, pcm.Length, time, dur);
        try { return mft.Process(s); }
        finally { MF.Release(s); }
    }

    public void Dispose() { mft.Dispose(); }
}

#endregion

#region MP4 fragmentado

class BoxW
{
    readonly MemoryStream s = new MemoryStream();
    readonly Stack<long> open = new Stack<long>();

    public long Pos { get { return s.Position; } }
    public BoxW U8(int v) { s.WriteByte((byte)v); return this; }
    public BoxW U16(int v) { U8(v >> 8); return U8(v); }
    public BoxW U24(int v) { U8(v >> 16); return U16(v); }
    public BoxW U32(long v) { U16((int)(v >> 16) & 0xFFFF); return U16((int)v & 0xFFFF); }
    public BoxW U64(long v) { U32((v >> 32) & 0xFFFFFFFF); return U32(v & 0xFFFFFFFF); }
    public BoxW Bytes(byte[] b) { s.Write(b, 0, b.Length); return this; }
    public BoxW Zeros(int n) { for (int i = 0; i < n; i++) s.WriteByte(0); return this; }
    public BoxW Ascii(string t) { return Bytes(Encoding.ASCII.GetBytes(t)); }
    public BoxW Open(string type) { open.Push(s.Position); U32(0); return Ascii(type); }
    public BoxW Full(string type, int ver, int flags) { Open(type); U8(ver); return U24(flags); }
    public BoxW Close()
    {
        long start = open.Pop(), end = s.Position;
        s.Position = start; U32(end - start); s.Position = end;
        return this;
    }
    public void Patch32(long pos, long v) { long end = s.Position; s.Position = pos; U32(v); s.Position = end; }
    public BoxW Matrix() { return U32(0x00010000).U32(0).U32(0).U32(0).U32(0x00010000).U32(0).U32(0).U32(0).U32(0x40000000); }
    public byte[] ToArray() { return s.ToArray(); }
}

static class Fmp4
{
    public const int VideoScale = 90000;

    public static byte[] Asc(int rate)
    {
        int[] rates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000 };
        int idx = Array.IndexOf(rates, rate);
        if (idx < 0) idx = 3;
        int v = (2 << 11) | (idx << 7) | (2 << 3); // AAC-LC, frecuencia, estereo
        return new byte[] { (byte)(v >> 8), (byte)v };
    }

    static void Dinf(BoxW b)
    {
        b.Open("dinf").Full("dref", 0, 0).U32(1).Full("url ", 0, 1).Close().Close().Close();
    }

    static void EmptyTables(BoxW b)
    {
        b.Full("stts", 0, 0).U32(0).Close();
        b.Full("stsc", 0, 0).U32(0).Close();
        b.Full("stsz", 0, 0).U32(0).U32(0).Close();
        b.Full("stco", 0, 0).U32(0).Close();
    }

    public static byte[] Init(int w, int h, byte[] sps, byte[] pps, bool audio, int arate)
    {
        BoxW b = new BoxW();
        b.Open("ftyp").Ascii("isom").U32(0x200).Ascii("isom").Ascii("iso6").Ascii("avc1").Ascii("mp41").Close();
        b.Open("moov");
        b.Full("mvhd", 0, 0).U32(0).U32(0).U32(1000).U32(0).U32(0x00010000).U16(0x0100).Zeros(10).Matrix().Zeros(24).U32(3).Close();

        // Pista de video
        b.Open("trak");
        b.Full("tkhd", 0, 3).U32(0).U32(0).U32(1).U32(0).U32(0).Zeros(8).U16(0).U16(0).U16(0).U16(0).Matrix().U32((long)w << 16).U32((long)h << 16).Close();
        b.Open("mdia");
        b.Full("mdhd", 0, 0).U32(0).U32(0).U32(VideoScale).U32(0).U16(0x55C4).U16(0).Close();
        b.Full("hdlr", 0, 0).U32(0).Ascii("vide").Zeros(12).Ascii("Video").U8(0).Close();
        b.Open("minf");
        b.Full("vmhd", 0, 1).U16(0).Zeros(6).Close();
        Dinf(b);
        b.Open("stbl");
        b.Full("stsd", 0, 0).U32(1);
        b.Open("avc1").Zeros(6).U16(1).Zeros(16).U16(w).U16(h).U32(0x00480000).U32(0x00480000).U32(0).U16(1).Zeros(32).U16(0x18).U16(0xFFFF);
        b.Open("avcC").U8(1).U8(sps[1]).U8(sps[2]).U8(sps[3]).U8(0xFF).U8(0xE1).U16(sps.Length).Bytes(sps).U8(1).U16(pps.Length).Bytes(pps).Close();
        b.Close(); // avc1
        b.Close(); // stsd
        EmptyTables(b);
        b.Close().Close().Close(); // stbl minf mdia
        b.Close(); // trak

        if (audio)
        {
            byte[] asc = Asc(arate);
            b.Open("trak");
            b.Full("tkhd", 0, 3).U32(0).U32(0).U32(2).U32(0).U32(0).Zeros(8).U16(0).U16(1).U16(0x0100).U16(0).Matrix().U32(0).U32(0).Close();
            b.Open("mdia");
            b.Full("mdhd", 0, 0).U32(0).U32(0).U32(arate).U32(0).U16(0x55C4).U16(0).Close();
            b.Full("hdlr", 0, 0).U32(0).Ascii("soun").Zeros(12).Ascii("Audio").U8(0).Close();
            b.Open("minf");
            b.Full("smhd", 0, 0).U16(0).U16(0).Close();
            Dinf(b);
            b.Open("stbl");
            b.Full("stsd", 0, 0).U32(1);
            b.Open("mp4a").Zeros(6).U16(1).Zeros(8).U16(2).U16(16).U16(0).U16(0).U32((long)arate << 16);
            int dcLen = 13 + 2 + asc.Length;
            int esLen = 3 + 2 + dcLen + 2 + 1;
            b.Full("esds", 0, 0)
             .U8(3).U8(esLen).U16(2).U8(0)
             .U8(4).U8(dcLen).U8(0x40).U8(0x15).U24(0).U32(192000).U32(192000)
             .U8(5).U8(asc.Length).Bytes(asc)
             .U8(6).U8(1).U8(2)
             .Close();
            b.Close(); // mp4a
            b.Close(); // stsd
            EmptyTables(b);
            b.Close().Close().Close();
            b.Close();
        }

        b.Open("mvex");
        b.Full("trex", 0, 0).U32(1).U32(1).U32(0).U32(0).U32(0).Close();
        if (audio) b.Full("trex", 0, 0).U32(2).U32(1).U32(0).U32(0).U32(0).Close();
        b.Close();
        b.Close(); // moov
        return b.ToArray();
    }

    static int seq;

    // Un fragmento (moof + mdat) con una o varias muestras de una pista.
    public static byte[] Fragment(int track, long baseTime, int[] durs, byte[][] samples, bool firstKey, bool video)
    {
        BoxW b = new BoxW();
        b.Open("moof");
        b.Full("mfhd", 0, 0).U32(Interlocked.Increment(ref seq)).Close();
        b.Open("traf");
        b.Full("tfhd", 0, 0x020000).U32(track).Close();
        b.Full("tfdt", 1, 0).U64(baseTime).Close();
        b.Full("trun", 0, 0x000701).U32(samples.Length);
        long offPos = b.Pos;
        b.U32(0);
        int total = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            int flags = !video ? 0x02000000 : (i == 0 && firstKey) ? 0x02000000 : 0x01010000;
            b.U32(durs[i]).U32(samples[i].Length).U32(flags);
            total += samples[i].Length;
        }
        b.Close().Close().Close(); // trun traf moof
        long moofSize = b.Pos;
        b.Patch32(offPos, moofSize + 8);
        b.U32(total + 8).Ascii("mdat");
        foreach (byte[] s in samples) b.Bytes(s);
        return b.ToArray();
    }
}

#endregion

// Reparte el stream a los clientes conectados; cada uno empieza en un fotograma clave.
class LiveHub
{
    readonly object lk = new object();
    byte[] init;
    string codecs;
    bool closed;
    public Action OnJoin;

    class Client { public readonly Queue<byte[]> Q = new Queue<byte[]>(); public bool Started, Dead; }
    readonly List<Client> clients = new List<Client>();

    public int Count { get { lock (lk) return clients.Count; } }
    public bool HasInit { get { lock (lk) return init != null; } }

    public void SetInit(byte[] i, string c) { lock (lk) { init = i; codecs = c; Monitor.PulseAll(lk); } }

    public void Publish(byte[] frag, bool video, bool key)
    {
        lock (lk)
        {
            foreach (Client c in clients)
            {
                if (!c.Started) { if (video && key) c.Started = true; else continue; }
                if (c.Q.Count > 400) c.Dead = true; // cliente demasiado lento: se reconectara
                else c.Q.Enqueue(frag);
            }
            Monitor.PulseAll(lk);
        }
    }

    public void Close() { lock (lk) { closed = true; Monitor.PulseAll(lk); } }

    public void Serve(Stream s)
    {
        Client c = new Client();
        byte[] ini; string cod;
        lock (lk)
        {
            int end = Environment.TickCount + 10000;
            while (!closed && init == null && unchecked(end - Environment.TickCount) > 0) Monitor.Wait(lk, 200);
            if (closed || init == null)
            {
                byte[] e = Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                s.Write(e, 0, e.Length);
                return;
            }
            ini = init; cod = codecs;
            clients.Add(c);
        }
        try
        {
            if (OnJoin != null) OnJoin();
            byte[] h = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nX-Codecs: " + cod +
                "\r\nAccess-Control-Expose-Headers: X-Codecs\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
            s.Write(ini, 0, ini.Length);
            while (true)
            {
                List<byte[]> batch;
                lock (lk)
                {
                    while (!closed && !c.Dead && c.Q.Count == 0) Monitor.Wait(lk, 500);
                    if (closed || c.Dead) return;
                    batch = new List<byte[]>(c.Q);
                    c.Q.Clear();
                }
                foreach (byte[] f in batch) s.Write(f, 0, f.Length);
            }
        }
        finally { lock (lk) clients.Remove(c); }
    }
}

// Una sesion de transmision: captura + codificacion de video y audio con un reloj comun.
class LiveSession
{
    public const int ARate = 48000;
    public readonly LiveHub Hub = new LiveHub();
    readonly Stopwatch clock = Stopwatch.StartNew();
    volatile bool running = true;
    readonly Rectangle bounds;
    readonly int outW, outH, fps, bitrate;
    public volatile string Error;
    volatile bool keyRequest, initReady, audioOk;
    readonly ManualResetEvent audioReady = new ManualResetEvent(false);

    // fotograma capturado pendiente de codificar (si el codificador va lento, se descarta el anterior)
    readonly object slotLk = new object();
    byte[] slot;
    long slotTime;
    readonly Queue<byte[]> pool = new Queue<byte[]>();

    readonly object pcmLk = new object();
    readonly Queue<byte[]> pcmQ = new Queue<byte[]>();
    double rpos; short lastL, lastR;

    // estadisticas
    int capN, encN, dropN, statCap, statEnc, statDrop;
    long capTicks, encTicks, outBytes, statBytes, statT;
    string profile = "", capMode = "";

    public LiveSession(Rectangle b, int maxH, int fps, int bitrate)
    {
        bounds = b;
        int w = b.Width & ~1, h = b.Height & ~1;
        if (h > maxH) { w = (int)((long)b.Width * maxH / b.Height) & ~1; h = maxH & ~1; }
        outW = w; outH = h;
        this.fps = fps; this.bitrate = bitrate;
        Hub.OnJoin = delegate { keyRequest = true; };
        Start(CaptureLoop);
        Start(EncodeLoop);
        Start(AudioLoop);
    }

    static void Start(ThreadStart f) { Thread t = new Thread(f); t.IsBackground = true; t.Start(); }

    public void Stop()
    {
        running = false;
        Hub.Close();
        lock (slotLk) Monitor.PulseAll(slotLk);
    }

    public string Stats()
    {
        long now = clock.ElapsedMilliseconds;
        double sec = Math.Max(0.001, (now - statT) / 1000.0);
        int c = capN - statCap, e = encN - statEnc, d = dropN - statDrop;
        long by = outBytes - statBytes;
        string r = string.Format("{0}x{1} {2} | captura " + capMode + " {3:0.0} fps ({4:0.0} ms) | codif {5:0.0} fps ({6:0.0} ms) | descartados {7} | {8:0.0} Mbps | audio {9}{10}",
            outW, outH, profile, c / sec, c > 0 ? capTicks / (double)c / TimeSpan.TicksPerMillisecond : 0,
            e / sec, e > 0 ? encTicks / (double)e / TimeSpan.TicksPerMillisecond : 0, d, by * 8 / sec / 1e6,
            audioOk ? "AAC" : "no", Error != null ? " | ERROR " + Error : "");
        statT = now; statCap = capN; statEnc = encN; statDrop = dropN; statBytes = outBytes;
        capTicks = 0; encTicks = 0;
        return r;
    }

    // ---------- video ----------

    void CaptureLoop()
    {
        Bitmap full = null, scaled = null;
        string dxErr;
        DxgiCapture dx = DxgiCapture.TryCreate(bounds, out dxErr);
        capMode = dx != null ? "DXGI" : "GDI(" + dxErr + ")";
        int dxRetry = 0;
        try
        {
            full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            bool scale = outW != (bounds.Width & ~1) || outH != (bounds.Height & ~1);
            if (scale) scaled = new Bitmap(outW, outH, PixelFormat.Format32bppRgb);
            for (int i = 0; i < 3; i++) pool.Enqueue(new byte[outW * outH * 3 / 2]);
            long interval = TimeSpan.TicksPerSecond / fps;
            long next = clock.Elapsed.Ticks;
            while (running)
            {
                long now = clock.Elapsed.Ticks;
                if (now < next) { Thread.Sleep((int)Math.Max(1, (next - now) / TimeSpan.TicksPerMillisecond)); continue; }
                next += interval;
                if (now - next > interval) next = now + interval; // vamos atrasados: no acumular
                long t = clock.Elapsed.Ticks;
                try
                {
                    bool got = false;
                    if (dx == null && ++dxRetry % (fps * 2) == 0) // reintentar DXGI cada ~2 s (p.ej. tras bloquear la sesion)
                    {
                        dx = DxgiCapture.TryCreate(bounds, out dxErr);
                        capMode = dx != null ? "DXGI" : "GDI(" + dxErr + ")";
                    }
                    if (dx != null)
                    {
                        try { got = dx.Capture(full); }
                        catch (Exception ex) { dx.Dispose(); dx = null; capMode = "GDI(" + ex.Message + ")"; }
                    }
                    using (Graphics g = Graphics.FromImage(full))
                    {
                        if (!got) g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                        ScreenServer.DrawCursor(g, bounds);
                    }
                    Bitmap src = full;
                    if (scaled != null)
                    {
                        using (Graphics g = Graphics.FromImage(scaled))
                        {
                            g.InterpolationMode = InterpolationMode.Bilinear;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(full, new Rectangle(0, 0, outW, outH), 0, 0, bounds.Width, bounds.Height, GraphicsUnit.Pixel);
                        }
                        src = scaled;
                    }
                    byte[] buf;
                    lock (slotLk) buf = pool.Count > 0 ? pool.Dequeue() : new byte[outW * outH * 3 / 2];
                    BitmapData bd = src.LockBits(new Rectangle(0, 0, outW, outH), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try { ToNV12(bd.Scan0, bd.Stride, outW, outH, buf); }
                    finally { src.UnlockBits(bd); }
                    lock (slotLk)
                    {
                        if (slot != null) { pool.Enqueue(slot); dropN++; }
                        slot = buf; slotTime = t;
                        Monitor.PulseAll(slotLk);
                    }
                    capN++;
                    capTicks += clock.Elapsed.Ticks - t;
                }
                catch { Thread.Sleep(100); } // pantalla bloqueada, UAC...
            }
        }
        catch (Exception ex) { Error = "captura: " + ex.Message; }
        finally
        {
            if (dx != null) dx.Dispose();
            if (full != null) full.Dispose();
            if (scaled != null) scaled.Dispose();
        }
    }

    // BGRA -> NV12 (BT.709, rango 16-235)
    static unsafe void ToNV12(IntPtr scan0, int stride, int w, int h, byte[] dst)
    {
        fixed (byte* d = dst)
        {
            byte* uvBase = d + w * h;
            for (int y = 0; y < h; y += 2)
            {
                byte* r0 = (byte*)scan0 + (long)y * stride;
                byte* r1 = r0 + stride;
                byte* y0 = d + y * w;
                byte* y1 = y0 + w;
                byte* uv = uvBase + (y >> 1) * w;
                for (int x = 0; x < w; x += 2)
                {
                    int b00 = r0[0], g00 = r0[1], R00 = r0[2], b01 = r0[4], g01 = r0[5], R01 = r0[6];
                    int b10 = r1[0], g10 = r1[1], R10 = r1[2], b11 = r1[4], g11 = r1[5], R11 = r1[6];
                    y0[0] = (byte)((47 * R00 + 157 * g00 + 16 * b00 + 4224) >> 8);
                    y0[1] = (byte)((47 * R01 + 157 * g01 + 16 * b01 + 4224) >> 8);
                    y1[0] = (byte)((47 * R10 + 157 * g10 + 16 * b10 + 4224) >> 8);
                    y1[1] = (byte)((47 * R11 + 157 * g11 + 16 * b11 + 4224) >> 8);
                    int rs = R00 + R01 + R10 + R11, gs = g00 + g01 + g10 + g11, bs = b00 + b01 + b10 + b11;
                    uv[0] = (byte)((-26 * rs - 87 * gs + 112 * bs + 131584) >> 10);
                    uv[1] = (byte)((112 * rs - 102 * gs - 10 * bs + 131584) >> 10);
                    r0 += 8; r1 += 8; y0 += 2; y1 += 2; uv += 2;
                }
            }
        }
    }

    void EncodeLoop()
    {
        H264Encoder enc = null;
        try
        {
            enc = new H264Encoder(outW, outH, fps, bitrate);
            profile = enc.Profile;
        }
        catch (Exception ex) { Error = "video: " + ex.Message; Hub.Close(); return; }
        audioReady.WaitOne(5000);
        byte[] sps = null, pps = null;
        long lastIdx = -1;
        long frameDur = TimeSpan.TicksPerSecond / fps;
        try
        {
            while (running)
            {
                byte[] buf; long t;
                lock (slotLk)
                {
                    while (running && slot == null) Monitor.Wait(slotLk, 200);
                    if (!running) break;
                    buf = slot; t = slotTime; slot = null;
                }
                if (keyRequest) { keyRequest = false; enc.ForceKey(); }
                long e0 = clock.Elapsed.Ticks;
                List<KeyValuePair<long, byte[]>> outs = enc.Encode(buf, t, frameDur);
                encTicks += clock.Elapsed.Ticks - e0;
                encN++;
                lock (slotLk) pool.Enqueue(buf);

                foreach (KeyValuePair<long, byte[]> o in outs)
                {
                    bool key;
                    byte[] avcc = ToAvcc(o.Value, ref sps, ref pps, out key);
                    if (!initReady)
                    {
                        if (!key || sps == null || pps == null) continue;
                        string codecs = "avc1." + sps[1].ToString("x2") + sps[2].ToString("x2") + sps[3].ToString("x2") + (audioOk ? ",mp4a.40.2" : "");
                        Hub.SetInit(Fmp4.Init(outW, outH, sps, pps, audioOk, ARate), codecs);
                        initReady = true;
                    }
                    // Tiempo ajustado a la rejilla de fotogramas: duracion exacta y sin huecos,
                    // asi cada fotograma se envia en cuanto sale del codificador (sin esperar al siguiente).
                    long idx = (o.Key * fps + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond;
                    if (idx <= lastIdx) idx = lastIdx + 1;
                    lastIdx = idx;
                    int dur = Fmp4.VideoScale / fps;
                    byte[] frag = Fmp4.Fragment(1, idx * Fmp4.VideoScale / fps, new int[] { dur }, new byte[][] { avcc }, key, true);
                    outBytes += frag.Length;
                    Hub.Publish(frag, true, key);
                }
            }
        }
        catch (Exception ex) { Error = "video: " + ex.ToString().Replace("\r\n", " / "); Hub.Close(); }
        finally { enc.Dispose(); }
    }

    // Annex-B -> AVCC (longitud + NAL); extrae SPS/PPS y detecta fotogramas clave.
    static byte[] ToAvcc(byte[] b, ref byte[] sps, ref byte[] pps, out bool key)
    {
        key = false;
        MemoryStream m = new MemoryStream(b.Length + 16);
        int n = b.Length, i = 0, start = -1;
        List<int[]> nals = new List<int[]>();
        while (i + 2 < n)
        {
            if (b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 1)
            {
                if (start >= 0) { int end = i; while (end > start && b[end - 1] == 0) end--; nals.Add(new int[] { start, end }); }
                start = i + 3; i += 3;
            }
            else i++;
        }
        if (start >= 0) nals.Add(new int[] { start, n });
        foreach (int[] r in nals)
        {
            int len = r[1] - r[0];
            if (len <= 0) continue;
            int type = b[r[0]] & 0x1F;
            if (type == 7) { sps = new byte[len]; Array.Copy(b, r[0], sps, 0, len); continue; }
            if (type == 8) { pps = new byte[len]; Array.Copy(b, r[0], pps, 0, len); continue; }
            if (type == 9) continue; // delimitador de unidad de acceso
            if (type == 5) key = true;
            m.WriteByte((byte)(len >> 24)); m.WriteByte((byte)(len >> 16)); m.WriteByte((byte)(len >> 8)); m.WriteByte((byte)len);
            m.Write(b, r[0], len);
        }
        return m.ToArray();
    }

    // ---------- audio ----------

    // PCM 16 bit estereo desde la captura del sistema (a cualquier frecuencia).
    public void PushPcm(byte[] pcm, int rate)
    {
        byte[] d = rate == ARate ? pcm : Resample(pcm, rate);
        lock (pcmLk)
        {
            pcmQ.Enqueue(d);
            while (pcmQ.Count > 100) pcmQ.Dequeue();
        }
    }

    byte[] Resample(byte[] inb, int inRate)
    {
        int n = inb.Length / 4;
        if (n == 0) return new byte[0];
        double step = inRate / (double)ARate;
        List<byte> o = new List<byte>((int)(n / step) * 4 + 8);
        double p = rpos;
        while (p < n)
        {
            int i = (int)p; double f = p - i;
            int aL = i == 0 ? lastL : BitConverter.ToInt16(inb, (i - 1) * 4), aR = i == 0 ? lastR : BitConverter.ToInt16(inb, (i - 1) * 4 + 2);
            int bL = BitConverter.ToInt16(inb, i * 4), bR = BitConverter.ToInt16(inb, i * 4 + 2);
            short L = (short)(aL + (bL - aL) * f), R = (short)(aR + (bR - aR) * f);
            o.Add((byte)L); o.Add((byte)(L >> 8)); o.Add((byte)R); o.Add((byte)(R >> 8));
            p += step;
        }
        rpos = p - n;
        lastL = BitConverter.ToInt16(inb, (n - 1) * 4); lastR = BitConverter.ToInt16(inb, (n - 1) * 4 + 2);
        return o.ToArray();
    }

    void AudioLoop()
    {
        AacEncoder aac = null;
        try { aac = new AacEncoder(ARate, 24000); audioOk = true; }
        catch (Exception ex) { Error = "audio: " + ex.Message; }
        audioReady.Set();
        if (aac == null) return;
        long fed = 0, frames = 0;
        try
        {
            while (running)
            {
                Thread.Sleep(10);
                long target = clock.Elapsed.Ticks * ARate / TimeSpan.TicksPerSecond;
                List<byte[]> chunks;
                lock (pcmLk) { chunks = new List<byte[]>(pcmQ); pcmQ.Clear(); }
                foreach (byte[] c in chunks)
                {
                    if (fed > target + ARate * 15 / 100) continue; // reloj del audio adelantado: descartar
                    Feed(aac, c, ref fed, ref frames);
                }
                // sin sonido en el PC no llegan datos: rellenar con silencio para mantener la linea de tiempo
                if (fed < target - ARate * 6 / 100)
                {
                    long pad = target - ARate * 3 / 100 - fed;
                    Feed(aac, new byte[pad * 4], ref fed, ref frames);
                }
            }
        }
        catch (Exception ex) { Error = "audio: " + ex.Message; }
        finally { aac.Dispose(); }
    }

    void Feed(AacEncoder aac, byte[] pcm, ref long fed, ref long frames)
    {
        int n = pcm.Length / 4;
        if (n == 0) return;
        List<KeyValuePair<long, byte[]>> outs = aac.Encode(pcm, fed * TimeSpan.TicksPerSecond / ARate, (long)n * TimeSpan.TicksPerSecond / ARate);
        fed += n;
        if (outs.Count == 0) return;
        long baseT = frames * 1024;
        frames += outs.Count;
        if (!initReady) return;
        int[] durs = new int[outs.Count];
        byte[][] data = new byte[outs.Count][];
        for (int i = 0; i < outs.Count; i++) { durs[i] = 1024; data[i] = outs[i].Value; }
        byte[] frag = Fmp4.Fragment(2, baseT, durs, data, false, false);
        outBytes += frag.Length;
        Hub.Publish(frag, false, false);
    }
}

