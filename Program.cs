// Compartir Pantalla en TV - transmite un monitor por WiFi a cualquier Smart TV con navegador.
// Compilar: ver build.cmd
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

static class Native
{
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
}

// Interfaces COM minimas de WASAPI para capturar lo que suena en el PC (loopback).
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long def, out long min);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr h);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out long devPos, out long qpcPos);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Per-monitor DPI: necesario para obtener los pixeles reales de cada pantalla.
        try { Native.SetProcessDpiAwareness(2); }
        catch { try { Native.SetProcessDPIAware(); } catch { } }
        int lt = Array.IndexOf(args, "--launchtest"); // diagnostico: --launchtest <ip> <archivo>
        if (lt >= 0 && args.Length > lt + 2)
        {
            Cast.Log = new StringBuilder();
            ScreenServer srv = new ScreenServer();
            srv.SetBounds(Screen.PrimaryScreen.Bounds);
            srv.Start(8080);
            int secs = 20;
            int li = Array.IndexOf(args, "--secs");
            if (li >= 0 && args.Length > li + 1) int.TryParse(args[li + 1], out secs);
            int t0 = Environment.TickCount;
            Thread mon = new Thread(delegate()
            {
                for (int k = 5; k <= secs; k += 5)
                {
                    Thread.Sleep(5000);
                    lock (Cast.Log)
                    {
                        Cast.Log.AppendLine("== t=" + k + "s clientes: " + srv.StreamClients + " | " + srv.LiveStats);
                        Cast.Log.AppendLine("   TV: " + srv.LastClientLog);
                    }
                }
            });
            mon.IsBackground = true;
            mon.Start();
            if (args[lt + 1] == "none") // solo servidor, sin enviar a la TV
            {
                Thread.Sleep(secs * 1000 + 500);
                srv.Stop();
                File.WriteAllText(args[lt + 2], Cast.Log.ToString());
                return;
            }
            try
            {
                Cast.Launch(args[lt + 1], 8080,
                    delegate { return unchecked(Environment.TickCount - t0) < secs * 1000; },
                    delegate(string m) { lock (Cast.Log) Cast.Log.AppendLine("ESTADO: " + m); });
                lock (Cast.Log) Cast.Log.AppendLine("TV devuelta al inicio");
            }
            catch (Exception ex) { lock (Cast.Log) Cast.Log.AppendLine("ERROR: " + ex.Message); }
            srv.Stop();
            File.WriteAllText(args[lt + 2], Cast.Log.ToString());
            return;
        }
        if (Array.IndexOf(args, "--discover") >= 0 && args.Length > 1)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in Cast.Discover()) sb.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(args[args.Length - 1], sb.Length > 0 ? sb.ToString() : "(ninguna)");
            return;
        }
        int probe = Array.IndexOf(args, "--probe"); // diagnostico: --probe <ip> <archivo>
        if (probe >= 0 && args.Length > probe + 2)
        {
            string res;
            try { res = Cast.Status(args[probe + 1]); } catch (Exception ex) { res = "ERROR: " + ex; }
            File.WriteAllText(args[probe + 2], res ?? "(sin respuesta)");
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(Array.IndexOf(args, "--autostart") >= 0));
    }
}

class ScreenServer
{
    const string Page =
        "<!doctype html><html><head><meta charset=utf-8>" +
        "<meta name=viewport content=\"width=device-width,initial-scale=1\"><title>Pantalla del PC</title>" +
        "<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}" +
        "img,video{width:100%;height:100%;object-fit:contain;display:block;background:#000}</style></head>" +
        "<body><video id=w playsinline></video><img id=v style=\"display:none\">" +
        "<div id=m style=\"display:none;position:fixed;left:0;right:0;bottom:0;padding:14px;background:rgba(0,0,0,.7);" +
        "color:#fff;font:18px sans-serif;text-align:center\">Toca la pantalla o pulsa OK para activar el sonido</div><script>" +
        "var m=document.getElementById('m'),w=document.getElementById('w'),mode='live',err='',lag=0,jumps=0,waits=0;" +
        // Colchon adaptativo: empieza en 0,4 s; cada parada lo sube 0,2 s (max 1,5 s);
        // cada 30 s sin paradas lo baja 0,05 s (min 0,3 s).
        "var T=0.4,TMIN=0.3,TMAX=1.5,started=false,calm=Date.now();" +
        "w.addEventListener('waiting',function(){if(!started)return;waits++;T=Math.min(TMAX,T+0.2);calm=Date.now();});" +
        "setInterval(function(){if(started&&Date.now()-calm>30000){T=Math.max(TMIN,T-0.05);calm=Date.now();}},1000);" +
        "function reload(e){err=String(e||'');setTimeout(function(){location.reload();},1500);}" +
        "function play(){var p=w.play();if(p&&p.catch)p.catch(function(){w.muted=true;m.style.display='block';w.play();});}" +
        "function unmute(){if(w.muted){w.muted=false;m.style.display='none';w.play();}}" +
        "document.addEventListener('click',unmute);document.addEventListener('keydown',unmute);" +
        // Modo principal: H.264 + AAC en MP4 fragmentado por Media Source Extensions (imagen y sonido sincronizados).
        "function live(){" +
        "if(!window.MediaSource||!window.fetch){legacy();return;}" +
        "fetch('/live.mp4').then(function(r){" +
        "var c=r.headers.get('X-Codecs'),mime='video/mp4; codecs=\"'+c+'\"';" +
        "if(r.status!=200||!r.body||!c||!MediaSource.isTypeSupported(mime)){err='no soportado '+c;legacy();return;}" +
        "var ms=new MediaSource(),sb=null,q=[],rd=r.body.getReader();" +
        "w.src=URL.createObjectURL(ms);" +
        "ms.addEventListener('sourceopen',function(){sb=ms.addSourceBuffer(mime);sb.addEventListener('updateend',feed);" +
        "sb.addEventListener('error',function(){reload('sb')});pump();});" +
        "function feed(){if(!sb||sb.updating||!q.length)return;var n=0,i;for(i=0;i<q.length;i++)n+=q[i].length;" +
        "var a=new Uint8Array(n),o=0;for(i=0;i<q.length;i++){a.set(q[i],o);o+=q[i].length;}q=[];" +
        "try{sb.appendBuffer(a);}catch(e){reload(e);}}" +
        "function pump(){rd.read().then(function(x){if(x.done){reload('fin');return;}q.push(x.value);feed();pump();},reload);}" +
        // Mantener ~0,2-0,4 s de colchon: si se acumula retraso, acelerar un poco o saltar al final.
        "setInterval(function(){if(!sb||!w.buffered.length)return;" +
        "var end=w.buffered.end(w.buffered.length-1);lag=end-w.currentTime;" +
        "if(!started){if(end-w.buffered.start(0)<T)return;started=true;calm=Date.now();w.currentTime=end-T;play();return;}" +
        // Mantener el colchon cerca de T: 1.03x si sobra mas de 0,3 s, 1x al volver a T; salto solo si sobra mucho.
        "if(lag>T+2){w.currentTime=end-T;jumps++;}else if(lag>T+0.3)w.playbackRate=1.03;else if(lag<=T)w.playbackRate=1;" +
        "if(w.paused&&lag>0.1)play();" +
        "var st=w.buffered.start(0);if(!sb.updating&&w.currentTime-st>30)try{sb.remove(st,w.currentTime-10);}catch(e){}" +
        "},200);" +
        "w.addEventListener('error',function(){reload('video')});" +
        "},function(e){err=String(e);legacy();});}" +
        // Informe al PC para diagnostico.
        "setInterval(function(){var qq=w.getVideoPlaybackQuality?w.getVideoPlaybackQuality():{};" +
        "try{fetch('/log?mode='+mode+'&lag='+lag.toFixed(2)+'&rs='+w.readyState+'&paused='+w.paused+'&muted='+w.muted+" +
        "'&frames='+(qq.totalVideoFrames||0)+'&dropped='+(qq.droppedVideoFrames||0)+'&rate='+w.playbackRate+'&ranges='+w.buffered.length+" +
        "'&waits='+waits+'&jumps='+jumps+'&target='+T.toFixed(2)+'&err='+encodeURIComponent(err));}catch(e){}},2000);" +
        "live();" +
        // Modo de respaldo: imagenes JPEG + audio PCM por separado.
        "function legacy(){mode='legacy';w.style.display='none';document.getElementById('v').style.display='block';" +
        // Audio: PCM 16 bit estereo por /audio, reproducido con WebAudio con ~120 ms de colchon.
        "var ac=null,t=0,AC=window.AudioContext||window.webkitAudioContext;" +
        "function unlock(){if(ac)ac.resume();}" +
        "document.addEventListener('click',unlock);document.addEventListener('keydown',unlock);" +
        "setInterval(function(){m.style.display=(ac&&ac.state=='suspended')?'block':'none';},1000);" +
        "function audio(){if(!window.fetch||!AC)return;" +
        "fetch('/audio').then(function(r){if(r.status!=200||!r.body){setTimeout(audio,5000);return;}" +
        "var rate=+r.headers.get('X-Rate')||48000;" +
        "if(!ac||ac.sampleRate!=rate){var o=ac;try{ac=new AC({sampleRate:rate});}catch(e){ac=new AC();}if(o)o.close();t=0;}" +
        "var rd=r.body.getReader(),rest=new Uint8Array(0);" +
        "function pump(){rd.read().then(function(x){if(x.done){setTimeout(audio,1000);return;}" +
        "var a=new Uint8Array(rest.length+x.value.length);a.set(rest);a.set(x.value,rest.length);" +
        "var n=a.length>>2;rest=a.slice(n*4);" +
        "if(n>0&&ac.state=='running'){var b=ac.createBuffer(2,n,rate),L=b.getChannelData(0),R=b.getChannelData(1)," +
        "d=new DataView(a.buffer,0,n*4);" +
        "for(var i=0;i<n;i++){L[i]=d.getInt16(i*4,true)/32768;R[i]=d.getInt16(i*4+2,true)/32768;}" +
        "var s=ac.createBufferSource();s.buffer=b;s.connect(ac.destination);var now=ac.currentTime;" +
        "if(t<now+0.02||t>now+0.5)t=now+0.12;s.start(t);t+=n/rate;}" +
        "pump();},function(){setTimeout(audio,1000);});}pump();},function(){setTimeout(audio,2000);});}" +
        "audio();" +
        "var v=document.getElementById('v'),poll=location.search.indexOf('poll')>=0;" +
        "function next(){v.src='/frame.jpg?t='+Date.now();}" +
        "function startPoll(){if(poll&&v.onload)return;poll=true;" +
        "v.onload=function(){setTimeout(next,30)};v.onerror=function(){setTimeout(next,800)};next();}" +
        "if(poll)startPoll();else{v.onerror=startPoll;" +
        "setTimeout(function(){if(!v.naturalWidth)startPoll();},4000);}" +
        "if(!poll)v.src='/stream';}" +
        "</script></body></html>";

    readonly object hub = new object();
    readonly List<TcpClient> clients = new List<TcpClient>();
    byte[] latest;
    long seq;
    int lastFrameTick;
    volatile int demandTick;
    volatile bool running;
    TcpListener listener;
    int streamClients;

    public volatile bool AudioEnabled = true;
    readonly List<Queue<byte[]>> audioQs = new List<Queue<byte[]>>();
    int audioRate = 48000;

    void Audio(NetworkStream s)
    {
        if (!AudioEnabled) { SendSimple(s, "404 Not Found", "text/plain", Encoding.ASCII.GetBytes("audio desactivado")); return; }
        Queue<byte[]> q = new Queue<byte[]>();
        int rate;
        lock (audioQs) { audioQs.Add(q); rate = audioRate; }
        try
        {
            byte[] h = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nX-Rate: " + rate +
                "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            s.Write(h, 0, h.Length);
            while (true)
            {
                byte[] d;
                lock (q)
                {
                    while (q.Count == 0)
                    {
                        if (!running || !AudioEnabled) return;
                        Monitor.Wait(q, 500);
                    }
                    d = q.Dequeue();
                }
                if (d == null) return; // cambio de dispositivo o formato: el cliente reconecta
                s.Write(d, 0, d.Length);
            }
        }
        finally { lock (audioQs) audioQs.Remove(q); }
    }

    void PublishAudio(byte[] d)
    {
        lock (audioQs)
            foreach (Queue<byte[]> q in audioQs)
                lock (q) { if (d == null || q.Count < 100) q.Enqueue(d); Monitor.Pulse(q); }
    }

    void AudioLoop()
    {
        while (running)
        {
            try { CaptureAudio(); } catch { }
            PublishAudio(null);
            for (int i = 0; i < 10 && running; i++) Thread.Sleep(100);
        }
    }

    static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    void CaptureAudio()
    {
        IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice dev;
        Check(en.GetDefaultAudioEndpoint(0, 0, out dev)); // eRender, eConsole
        Guid iid = typeof(IAudioClient).GUID;
        object o;
        Check(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
        IAudioClient ac = (IAudioClient)o;
        IntPtr pf;
        Check(ac.GetMixFormat(out pf));
        int tag = (ushort)Marshal.ReadInt16(pf, 0), ch = Marshal.ReadInt16(pf, 2), rate = Marshal.ReadInt32(pf, 4);
        int bits = Marshal.ReadInt16(pf, 14);
        if (tag == 0xFFFE) tag = (ushort)Marshal.ReadInt16(pf, 24); // WAVE_FORMAT_EXTENSIBLE -> SubFormat
        bool isFloat = tag == 3 && bits == 32, isPcm = tag == 1 && bits == 16;
        try
        {
            if (!isFloat && !isPcm) throw new NotSupportedException();
            Check(ac.Initialize(0, 0x00020000, 2000000, 0, pf, IntPtr.Zero)); // compartido + LOOPBACK, 200 ms
        }
        finally { Marshal.FreeCoTaskMem(pf); }
        iid = typeof(IAudioCaptureClient).GUID;
        Check(ac.GetService(ref iid, out o));
        IAudioCaptureClient cc = (IAudioCaptureClient)o;
        lock (audioQs) audioRate = rate;
        PublishAudio(null); // los clientes reconectan y leen la frecuencia correcta
        Check(ac.Start());
        try
        {
            float[] fb = new float[0];
            short[] sb = new short[0];
            while (running)
            {
                Thread.Sleep(10);
                uint n;
                Check(cc.GetNextPacketSize(out n));
                while (n > 0)
                {
                    IntPtr data; uint frames, flags; long p1, p2;
                    Check(cc.GetBuffer(out data, out frames, out flags, out p1, out p2));
                    int fr = (int)frames, total = fr * ch;
                    byte[] outb = new byte[fr * 4];
                    if ((flags & 2) == 0) // no es silencio
                    {
                        if (isFloat)
                        {
                            if (fb.Length < total) fb = new float[total];
                            Marshal.Copy(data, fb, 0, total);
                            for (int i = 0; i < fr; i++)
                            {
                                float l = fb[i * ch], r = ch > 1 ? fb[i * ch + 1] : l;
                                int li = (int)(l * 32767f), ri = (int)(r * 32767f);
                                if (li > 32767) li = 32767; else if (li < -32768) li = -32768;
                                if (ri > 32767) ri = 32767; else if (ri < -32768) ri = -32768;
                                outb[i * 4] = (byte)li; outb[i * 4 + 1] = (byte)(li >> 8);
                                outb[i * 4 + 2] = (byte)ri; outb[i * 4 + 3] = (byte)(ri >> 8);
                            }
                        }
                        else
                        {
                            if (sb.Length < total) sb = new short[total];
                            Marshal.Copy(data, sb, 0, total);
                            for (int i = 0; i < fr; i++)
                            {
                                short l = sb[i * ch], r = ch > 1 ? sb[i * ch + 1] : l;
                                outb[i * 4] = (byte)l; outb[i * 4 + 1] = (byte)(l >> 8);
                                outb[i * 4 + 2] = (byte)r; outb[i * 4 + 3] = (byte)(r >> 8);
                            }
                        }
                    }
                    Check(cc.ReleaseBuffer(frames));
                    if (AudioEnabled)
                    {
                        PublishAudio(outb);
                        LiveSession ls = live;
                        if (ls != null) ls.PushPcm(outb, rate);
                    }
                    Check(cc.GetNextPacketSize(out n));
                }
            }
        }
        finally
        {
            try { ac.Stop(); } catch { }
            Marshal.ReleaseComObject(cc);
            Marshal.ReleaseComObject(ac);
            Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
        }
    }

    public volatile int Fps = 30;
    public volatile int Quality = 70;
    public volatile int MaxHeight = 1080;
    public volatile int Bitrate = 12000000;
    public volatile string LastClientLog = "";
    Rectangle bounds;
    volatile LiveSession live;

    public string LiveStats { get { LiveSession ls = live; return ls == null ? "(sin sesion)" : ls.Stats(); } }

    // (Re)inicia la transmision H.264 con la pantalla y calidad actuales; la TV se reconecta sola.
    public void RestartLive()
    {
        if (!running) return;
        LiveSession old = live;
        live = null;
        if (old != null) old.Stop();
        Rectangle b;
        lock (hub) b = bounds;
        if (b.Width > 0 && b.Height > 0) live = new LiveSession(b, MaxHeight, Fps, Bitrate);
    }

    volatile int lastPollTick = Environment.TickCount - 100000;
    // Clientes H.264 + MJPEG + 1 si alguien esta pidiendo imagenes sueltas (modo respaldo) en los ultimos 3 s.
    public int StreamClients
    {
        get
        {
            LiveSession ls = live;
            return (ls != null ? ls.Hub.Count : 0) + streamClients + (unchecked(Environment.TickCount - lastPollTick) < 3000 ? 1 : 0);
        }
    }
    public int AudioClients { get { lock (audioQs) return audioQs.Count; } }
    public bool Running { get { return running; } }

    public void SetBounds(Rectangle r) { lock (hub) { bounds = r; } }

    public void Start(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        running = true;
        demandTick = Environment.TickCount - 10000;
        StartThread(AcceptLoop);
        StartThread(CaptureLoop);
        StartThread(AudioLoop);
        RestartLive();
    }

    public void Stop()
    {
        running = false;
        LiveSession ls = live;
        live = null;
        if (ls != null) ls.Stop();
        try { listener.Stop(); } catch { }
        lock (clients) { foreach (TcpClient c in clients) { try { c.Close(); } catch { } } clients.Clear(); }
        lock (hub) { latest = null; Monitor.PulseAll(hub); }
    }

    static void StartThread(ThreadStart f)
    {
        Thread t = new Thread(f);
        t.IsBackground = true;
        t.Start();
    }

    void AcceptLoop()
    {
        while (running)
        {
            TcpClient c;
            try { c = listener.AcceptTcpClient(); }
            catch { break; }
            lock (clients) clients.Add(c);
            TcpClient cc = c;
            StartThread(delegate { Handle(cc); });
        }
    }

    void Handle(TcpClient c)
    {
        try
        {
            c.NoDelay = true;
            c.ReceiveTimeout = 5000;
            c.SendTimeout = 5000;
            NetworkStream s = c.GetStream();
            string full = ReadPath(s);
            if (full == null) return;
            int qi = full.IndexOf('?');
            string path = qi >= 0 ? full.Substring(0, qi) : full;
            if (path == "/live.mp4")
            {
                LiveSession ls = live;
                if (ls == null) SendSimple(s, "503 Service Unavailable", "text/plain", new byte[0]);
                else ls.Hub.Serve(s);
            }
            else if (path == "/log")
            {
                LastClientLog = DateTime.Now.ToString("HH:mm:ss ") + (qi >= 0 ? Uri.UnescapeDataString(full.Substring(qi + 1)) : "");
                SendSimple(s, "200 OK", "text/plain", new byte[0]);
            }
            else if (path == "/" || path == "/index.html") SendSimple(s, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page));
            else if (path == "/stream") Stream(s);
            else if (path == "/frame.jpg") SingleFrame(s);
            else if (path == "/audio") Audio(s);
            else SendSimple(s, "404 Not Found", "text/plain", Encoding.ASCII.GetBytes("404"));
        }
        catch { }
        finally
        {
            lock (clients) clients.Remove(c);
            try { c.Close(); } catch { }
        }
    }

    static string ReadPath(NetworkStream s)
    {
        byte[] buf = new byte[8192];
        int n = 0;
        while (n < buf.Length)
        {
            int r = s.Read(buf, n, buf.Length - n);
            if (r <= 0) return null;
            n += r;
            if (n >= 4 && Encoding.ASCII.GetString(buf, 0, n).IndexOf("\r\n\r\n") >= 0) break;
        }
        string req = Encoding.ASCII.GetString(buf, 0, n);
        string[] parts = req.Split(new char[] { '\r', '\n' })[0].Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;
        return parts[1];
    }

    static void SendSimple(NetworkStream s, string status, string type, byte[] body)
    {
        byte[] h = Encoding.ASCII.GetBytes(
            "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
            "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        s.Write(h, 0, h.Length);
        s.Write(body, 0, body.Length);
    }

    void SingleFrame(NetworkStream s)
    {
        byte[] f;
        lastPollTick = Environment.TickCount;
        lock (hub)
        {
            demandTick = Environment.TickCount;
            if (latest == null || unchecked(Environment.TickCount - lastFrameTick) > 500) Monitor.Wait(hub, 1500);
            f = latest;
        }
        if (f == null) SendSimple(s, "503 Service Unavailable", "text/plain", Encoding.ASCII.GetBytes("sin imagen"));
        else SendSimple(s, "200 OK", "image/jpeg", f);
    }

    void Stream(NetworkStream s)
    {
        byte[] h = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary=frame\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        s.Write(h, 0, h.Length);
        Interlocked.Increment(ref streamClients);
        try
        {
            long last = -1;
            byte[] tail = Encoding.ASCII.GetBytes("\r\n");
            while (running)
            {
                byte[] f;
                lock (hub)
                {
                    while (running && (seq == last || latest == null))
                    {
                        demandTick = Environment.TickCount;
                        Monitor.Wait(hub, 500);
                    }
                    demandTick = Environment.TickCount;
                    f = latest;
                    last = seq;
                }
                if (f == null) break;
                byte[] ph = Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\nContent-Length: " + f.Length + "\r\n\r\n");
                s.Write(ph, 0, ph.Length);
                s.Write(f, 0, f.Length);
                s.Write(tail, 0, tail.Length);
            }
        }
        finally { Interlocked.Decrement(ref streamClients); }
    }

    void CaptureLoop()
    {
        ImageCodecInfo jpeg = null;
        foreach (ImageCodecInfo ci in ImageCodecInfo.GetImageEncoders())
            if (ci.FormatID == ImageFormat.Jpeg.Guid) jpeg = ci;
        Bitmap full = null, scaled = null;
        MemoryStream ms = new MemoryStream(1 << 20);

        while (running)
        {
            int t0 = Environment.TickCount;
            if (unchecked(t0 - demandTick) > 2000) { Thread.Sleep(100); continue; } // nadie mirando
            try
            {
                Rectangle b;
                lock (hub) b = bounds;
                if (full == null || full.Width != b.Width || full.Height != b.Height)
                {
                    if (full != null) full.Dispose();
                    full = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppRgb);
                }
                using (Graphics g = Graphics.FromImage(full))
                {
                    g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
                    DrawCursor(g, b);
                }
                Bitmap outBmp = full;
                int maxH = MaxHeight;
                if (b.Height > maxH)
                {
                    int w = (b.Width * maxH / b.Height) & ~1;
                    if (scaled == null || scaled.Width != w || scaled.Height != maxH)
                    {
                        if (scaled != null) scaled.Dispose();
                        scaled = new Bitmap(w, maxH, PixelFormat.Format24bppRgb);
                    }
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(full, new Rectangle(0, 0, w, maxH), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
                    }
                    outBmp = scaled;
                }
                ms.SetLength(0);
                using (EncoderParameters ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Quality);
                    outBmp.Save(ms, jpeg, ep);
                }
                byte[] data = ms.ToArray();
                lock (hub)
                {
                    latest = data;
                    seq++;
                    lastFrameTick = Environment.TickCount;
                    Monitor.PulseAll(hub);
                }
            }
            catch { Thread.Sleep(300); } // p.ej. pantalla bloqueada o UAC
            int wait = 1000 / Math.Max(1, Fps) - unchecked(Environment.TickCount - t0);
            if (wait > 0) Thread.Sleep(wait);
        }
        if (full != null) full.Dispose();
        if (scaled != null) scaled.Dispose();
    }

    internal static void DrawCursor(Graphics g, Rectangle b)
    {
        Native.CURSORINFO ci = new Native.CURSORINFO();
        ci.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
        int hx = 0, hy = 0;
        Native.ICONINFO ii;
        if (Native.GetIconInfo(ci.hCursor, out ii))
        {
            hx = ii.xHotspot; hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
        }
        int x = ci.ptScreenPos.x - b.X - hx, y = ci.ptScreenPos.y - b.Y - hy;
        if (x < -64 || y < -64 || x > b.Width || y > b.Height) return;
        IntPtr hdc = g.GetHdc();
        try { Native.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
        finally { g.ReleaseHdc(hdc); }
    }
}

// Conexion Google Cast (CASTV2): TLS al puerto 8009 con mensajes protobuf.
class CastConn : IDisposable
{
    readonly TcpClient tcp;
    readonly SslStream ssl;
    public readonly IPAddress LocalIp;

    public CastConn(string ip)
    {
        tcp = new TcpClient();
        IAsyncResult ar = tcp.BeginConnect(ip, 8009, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(4000)) { tcp.Close(); throw new Exception("La TV no responde (puerto 8009)."); }
        tcp.EndConnect(ar);
        LocalIp = ((IPEndPoint)tcp.Client.LocalEndPoint).Address;
        ssl = new SslStream(tcp.GetStream(), false, delegate { return true; }); // la TV usa certificado propio
        ssl.AuthenticateAsClient(ip, null, SslProtocols.Tls12, false);
        ssl.ReadTimeout = 15000;
        ssl.WriteTimeout = 5000;
    }

    static void WriteVarint(MemoryStream m, int v)
    {
        while (v >= 0x80) { m.WriteByte((byte)(v | 0x80)); v >>= 7; }
        m.WriteByte((byte)v);
    }

    static void WriteStr(MemoryStream m, int field, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        m.WriteByte((byte)(field << 3 | 2));
        WriteVarint(m, b.Length);
        m.Write(b, 0, b.Length);
    }

    public void Send(string dest, string ns, string json)
    {
        MemoryStream m = new MemoryStream();
        m.Write(new byte[4], 0, 4);
        m.WriteByte(0x08); m.WriteByte(0);      // protocol_version = CASTV2_1_0
        WriteStr(m, 2, "sender-0");
        WriteStr(m, 3, dest);
        WriteStr(m, 4, ns);
        m.WriteByte(0x28); m.WriteByte(0);      // payload_type = STRING
        WriteStr(m, 6, json);
        byte[] buf = m.ToArray();
        int len = buf.Length - 4;
        buf[0] = (byte)(len >> 24); buf[1] = (byte)(len >> 16); buf[2] = (byte)(len >> 8); buf[3] = (byte)len;
        lock (this) // el hilo de latidos y el lector pueden escribir a la vez
        {
            ssl.Write(buf, 0, buf.Length);
            ssl.Flush();
        }
    }

    void ReadFull(byte[] b, int n)
    {
        int got = 0;
        while (got < n)
        {
            int r = ssl.Read(b, got, n - got);
            if (r <= 0) throw new IOException("La TV cerró la conexión.");
            got += r;
        }
    }

    public void Receive(out string ns, out string payload)
    {
        ns = ""; payload = "";
        byte[] h = new byte[4];
        ReadFull(h, 4);
        int len = h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3];
        if (len < 0 || len > 1 << 20) throw new IOException("Mensaje inválido de la TV.");
        byte[] b = new byte[len];
        ReadFull(b, len);
        int p = 0;
        while (p < len)
        {
            int tag = ReadVarint(b, ref p);
            int field = tag >> 3, wt = tag & 7;
            if (wt == 0) ReadVarint(b, ref p);
            else if (wt == 1) p += 8;
            else if (wt == 5) p += 4;
            else if (wt == 2)
            {
                int l = ReadVarint(b, ref p);
                if (field == 4) ns = Encoding.UTF8.GetString(b, p, l);
                else if (field == 6) payload = Encoding.UTF8.GetString(b, p, l);
                p += l;
            }
            else break;
        }
    }

    static int ReadVarint(byte[] b, ref int p)
    {
        int v = 0, shift = 0;
        while (p < b.Length)
        {
            byte x = b[p++];
            if (shift < 32) v |= (x & 0x7F) << shift;
            shift += 7;
            if ((x & 0x80) == 0) break;
        }
        return v;
    }

    public void Dispose()
    {
        try { ssl.Close(); } catch { }
        try { tcp.Close(); } catch { }
    }
}

static class Cast
{
    // DashCast: receptor publico que abre una URL cualquiera en la TV.
    const string AppId = "84912283";
    const string NsDashDefault = "urn:x-cast:com.madmod.dashcast";
    const string NsConn = "urn:x-cast:com.google.cast.tp.connection";
    const string NsRecv = "urn:x-cast:com.google.cast.receiver";
    const string NsBeat = "urn:x-cast:com.google.cast.tp.heartbeat";
    const string Connect = "{\"type\":\"CONNECT\"}";

    public static StringBuilder Log;
    static void L(string s) { if (Log != null) lock (Log) Log.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff ") + s); }

    static bool HasApp(string p) { return Regex.IsMatch(p, "\"appId\"\\s*:\\s*\"" + AppId + "\""); }
    static string Field(string p, string name)
    {
        Match m = Regex.Match(p, "\"" + name + "\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    // Lee mensajes hasta que 'accept' devuelve true; contesta los PING.
    static string WaitFor(CastConn c, int ms, Predicate<string> accept)
    {
        int end = Environment.TickCount + ms;
        while (unchecked(end - Environment.TickCount) > 0)
        {
            string ns, p;
            c.Receive(out ns, out p);
            L("<- " + ns + " " + p);
            if (ns == NsBeat) { if (p.Contains("PING")) c.Send("receiver-0", NsBeat, "{\"type\":\"PONG\"}"); continue; }
            if (p.Contains("LAUNCH_ERROR")) throw new Exception("La TV rechazó abrir el receptor: " + p);
            if (ns == NsRecv && p.Contains("RECEIVER_STATUS") && accept(p)) return p;
        }
        throw new Exception("La TV no respondió a tiempo.");
    }

    public static string Status(string tvIp)
    {
        using (CastConn c = new CastConn(tvIp))
        {
            c.Send("receiver-0", NsConn, Connect);
            c.Send("receiver-0", NsRecv, "{\"type\":\"GET_STATUS\",\"requestId\":2}");
            return WaitFor(c, 6000, delegate { return true; });
        }
    }

    // Abre en la TV la pagina del stream servida por este PC y mantiene la sesion Cast abierta
    // mientras active() sea true (si el emisor se desconecta, la TV cierra el receptor).
    // Al terminar cierra el receptor y la TV vuelve a su inicio. status() informa del progreso.
    public static void Launch(string tvIp, int port, Func<bool> active, Action<string> status)
    {
        using (CastConn c = new CastConn(tvIp))
        {
            status("Abriendo el receptor en la TV...");
            string url = "http://" + c.LocalIp + ":" + port + "/";
            c.Send("receiver-0", NsConn, Connect);
            c.Send("receiver-0", NsRecv, "{\"type\":\"LAUNCH\",\"appId\":\"" + AppId + "\",\"requestId\":1}");
            string st = WaitFor(c, 30000, delegate(string p)
            {
                return HasApp(p) && Regex.IsMatch(p, "urn:x-cast:[\\w.]*dashcast") && p.Contains("\"transportId\"");
            });
            string transport = Field(st, "transportId");
            if (transport == null) throw new Exception("Respuesta inesperada de la TV.");
            Match nm = Regex.Match(st, "urn:x-cast:[\\w.]*dashcast");
            string nsDash = nm.Success ? nm.Value : NsDashDefault;
            L("transport " + transport + " ns " + nsDash);
            Thread.Sleep(500);
            c.Send(transport, NsConn, Connect);
            c.Send(transport, nsDash,"{\"url\":\"" + url + "\",\"force\":true,\"reload\":false,\"reload_time\":0}");
            L("-> load " + url);
            status("Enviado a la TV, esperando la imagen...");
            string session = Field(st, "sessionId");

            // Latidos + parada: hilo aparte; el hilo actual solo lee.
            bool closedByTv = false, done = false;
            Thread beat = new Thread(delegate()
            {
                int last = Environment.TickCount;
                while (!done)
                {
                    if (!active())
                    {
                        try
                        {
                            if (session != null)
                                c.Send("receiver-0", NsRecv, "{\"type\":\"STOP\",\"sessionId\":\"" + session + "\",\"requestId\":3}");
                            Thread.Sleep(400);
                        }
                        catch { }
                        c.Dispose(); // desbloquea la lectura
                        return;
                    }
                    if (unchecked(Environment.TickCount - last) > 5000)
                    {
                        last = Environment.TickCount;
                        try { c.Send("receiver-0", NsBeat, "{\"type\":\"PING\"}"); } catch { }
                    }
                    Thread.Sleep(200);
                }
            });
            beat.IsBackground = true;
            beat.Start();
            try
            {
                while (true)
                {
                    string ns, p;
                    c.Receive(out ns, out p);
                    L("<- " + ns + " " + p);
                    if (ns == NsBeat) { if (p.Contains("PING")) c.Send("receiver-0", NsBeat, "{\"type\":\"PONG\"}"); continue; }
                    if (ns == NsRecv && p.Contains("RECEIVER_STATUS") && !HasApp(p)) { closedByTv = true; break; }
                }
            }
            catch (Exception ex) { L("fin: " + ex.Message); }
            done = true;
            if (active()) throw new Exception(closedByTv ? "La transmisión se cerró en la TV." : "Se perdió la conexión con la TV.");
        }
    }

    // Cierra el receptor y devuelve la TV a su pantalla de inicio.
    public static void Stop(string tvIp)
    {
        using (CastConn c = new CastConn(tvIp))
        {
            c.Send("receiver-0", NsConn, Connect);
            c.Send("receiver-0", NsRecv, "{\"type\":\"GET_STATUS\",\"requestId\":2}");
            string st = WaitFor(c, 6000, delegate { return true; });
            string session = Field(st, "sessionId");
            if (!HasApp(st) || session == null) return;
            c.Send("receiver-0", NsRecv, "{\"type\":\"STOP\",\"sessionId\":\"" + session + "\",\"requestId\":3}");
            Thread.Sleep(400);
        }
    }

    // Busca por SSDP los dispositivos con Google Cast. Devuelve pares (nombre, ip).
    public static List<KeyValuePair<string, string>> Discover()
    {
        byte[] msg = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
            "ST: urn:dial-multiscreen-org:service:dial:1\r\n\r\n");
        IPEndPoint dst = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        List<UdpClient> socks = new List<UdpClient>();
        foreach (string ip in MainForm.LocalIps())
        {
            try
            {
                UdpClient u = new UdpClient(new IPEndPoint(IPAddress.Parse(ip), 0));
                u.Send(msg, msg.Length, dst);
                u.Send(msg, msg.Length, dst);
                socks.Add(u);
            }
            catch { }
        }
        Dictionary<string, string> locs = new Dictionary<string, string>();
        int end = Environment.TickCount + 3000;
        while (unchecked(end - Environment.TickCount) > 0)
        {
            foreach (UdpClient u in socks)
            {
                try
                {
                    while (u.Available > 0)
                    {
                        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        string r = Encoding.ASCII.GetString(u.Receive(ref ep));
                        Match m = Regex.Match(r, "^LOCATION:\\s*(\\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if (m.Success) locs[ep.Address.ToString()] = m.Groups[1].Value;
                    }
                }
                catch { }
            }
            Thread.Sleep(50);
        }
        foreach (UdpClient u in socks) { try { u.Close(); } catch { } }

        // Muchas TVs no contestan siempre al anuncio SSDP: buscar tambien el puerto Cast (8009) en la subred.
        foreach (string ip in ScanPort8009()) if (!locs.ContainsKey(ip)) locs[ip] = null;

        List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
        foreach (KeyValuePair<string, string> kv in locs)
        {
            string name = null;
            string body = Get("http://" + kv.Key + ":8008/setup/eureka_info?params=name");
            Match m = body == null ? Match.Empty : Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success) name = Regex.Unescape(m.Groups[1].Value);
            else if (kv.Value != null)
            {
                body = Get(kv.Value);
                m = body == null ? Match.Empty : Regex.Match(body, "<friendlyName>(.*?)</friendlyName>", RegexOptions.Singleline);
                if (m.Success) name = WebUtility.HtmlDecode(m.Groups[1].Value);
            }
            if (name == null && kv.Value == null) continue; // puerto 8009 abierto pero no es un Chromecast
            found.Add(new KeyValuePair<string, string>(name ?? kv.Key, kv.Key));
        }
        return found;
    }

    static string Get(string url)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 2500;
            req.Proxy = null;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch { return null; }
    }

    static List<string> ScanPort8009()
    {
        List<string> open = new List<string>();
        List<KeyValuePair<string, TcpClient>> conns = new List<KeyValuePair<string, TcpClient>>();
        List<IAsyncResult> waits = new List<IAsyncResult>();
        foreach (string local in MainForm.LocalIps())
        {
            string[] o = local.Split('.');
            if (o.Length != 4) continue;
            // solo redes domesticas tipicas /24, no adaptadores virtuales sin puerta de enlace
            if (!MainForm.HasGateway(local)) continue;
            for (int i = 1; i < 255; i++)
            {
                string ip = o[0] + "." + o[1] + "." + o[2] + "." + i;
                if (ip == local) continue;
                TcpClient c = new TcpClient();
                try { waits.Add(c.BeginConnect(ip, 8009, null, null)); conns.Add(new KeyValuePair<string, TcpClient>(ip, c)); }
                catch { c.Close(); }
            }
        }
        Thread.Sleep(2500);
        for (int i = 0; i < conns.Count; i++)
        {
            try { if (waits[i].IsCompleted && conns[i].Value.Connected) open.Add(conns[i].Key); } catch { }
            try { conns[i].Value.Close(); } catch { }
        }
        return open;
    }
}

class MainForm : Form
{
    readonly ComboBox cmbTv = new ComboBox();
    readonly Button btnScan = new Button();
    readonly Panel body = new Panel();
    List<string> tvIps = new List<string>();
    Control host;
    string castedIp, castMsg;
    readonly ScreenServer server = new ScreenServer();
    readonly ComboBox cmbScreen = new ComboBox();
    readonly ComboBox cmbQuality = new ComboBox();
    readonly ComboBox cmbFps = new ComboBox();
    readonly NumericUpDown numPort = new NumericUpDown();
    readonly Button btnStart = new Button();
    readonly Button btnRefresh = new Button();
    readonly TextBox txtUrl = new TextBox();
    readonly Label lblStatus = new Label();
    readonly Label lblOther = new Label();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    Screen[] screens;

    static readonly int[] QualityJpeg = { 85, 75, 60, 50 };
    static readonly int[] QualityMaxH = { 1440, 1080, 1080, 720 };
    static readonly int[] QualityBitrate = { 20000000, 12000000, 6000000, 3000000 };
    static readonly int[] FpsValues = { 24, 30, 60 };

    public MainForm(bool autostart)
    {
        SuspendLayout();
        Text = "Compartir Pantalla en TV";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 406);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        host = this;
        AddLabel("Enviar a la TV:", 12, 12);
        cmbTv.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbTv.SetBounds(12, 32, 340, 24);
        btnScan.SetBounds(360, 31, 88, 26);
        btnScan.Click += delegate { ScanTvs(); };
        FillTvs(new List<KeyValuePair<string, string>>());
        body.SetBounds(0, 56, 460, 350);
        Controls.AddRange(new Control[] { cmbTv, btnScan, body });
        host = body;

        CheckBox chkAudio = new CheckBox();
        chkAudio.Text = "Transmitir audio del PC";
        chkAudio.AutoSize = true;
        chkAudio.Checked = true;
        chkAudio.Location = new Point(200, 10);
        chkAudio.CheckedChanged += delegate { server.AudioEnabled = chkAudio.Checked; };
        body.Controls.Add(chkAudio);

        AddLabel("Pantalla a compartir:", 12, 12);
        cmbScreen.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbScreen.SetBounds(12, 32, 340, 24);
        cmbScreen.SelectedIndexChanged += delegate { ApplyScreen(); };
        btnRefresh.Text = "Actualizar";
        btnRefresh.SetBounds(360, 31, 88, 26);
        btnRefresh.Click += delegate { LoadScreens(); };

        AddLabel("Calidad:", 12, 68);
        cmbQuality.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbQuality.Items.AddRange(new object[] { "Máxima (hasta 1440p)", "Alta (1080p)", "Media (1080p)", "Baja (720p)" });
        cmbQuality.SetBounds(12, 88, 180, 24);
        cmbQuality.SelectedIndex = 1;
        cmbQuality.SelectedIndexChanged += delegate { ApplyQuality(); };

        AddLabel("Imágenes/seg:", 204, 68);
        cmbFps.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFps.Items.AddRange(new object[] { "24", "30", "60" });
        cmbFps.SetBounds(204, 88, 100, 24);
        cmbFps.SelectedIndex = 1;
        cmbFps.SelectedIndexChanged += delegate { ApplyQuality(); };

        AddLabel("Puerto:", 316, 68);
        numPort.Minimum = 1024; numPort.Maximum = 65535; numPort.Value = 8080;
        numPort.SetBounds(316, 88, 132, 24);

        btnStart.Text = "▶  Iniciar transmisión";
        btnStart.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        btnStart.SetBounds(12, 128, 436, 44);
        btnStart.Click += delegate { Toggle(); };

        AddLabel("Dirección del stream (para abrirla a mano en otro dispositivo):", 12, 186);
        txtUrl.ReadOnly = true;
        txtUrl.Font = new Font("Consolas", 16f, FontStyle.Bold);
        txtUrl.TextAlign = HorizontalAlignment.Center;
        txtUrl.SetBounds(12, 206, 436, 34);

        lblOther.SetBounds(12, 244, 436, 18);
        lblOther.ForeColor = SystemColors.GrayText;
        lblStatus.SetBounds(12, 266, 436, 20);
        lblStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

        Label help = new Label();
        help.SetBounds(12, 292, 436, 50);
        help.ForeColor = SystemColors.GrayText;
        help.Text = "El PC y la TV deben estar en la misma red. Si Windows pregunta por el firewall, " +
                    "permite el acceso en redes privadas. La imagen aparece sola en la TV elegida; no hay que tocar nada en ella.";

        body.Controls.AddRange(new Control[] { cmbScreen, btnRefresh, cmbQuality, cmbFps, numPort, btnStart, txtUrl, lblOther, lblStatus, help });
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        LoadScreens();
        ApplyQuality();
        UpdateUi();
        timer.Interval = 1000;
        timer.Tick += delegate { UpdateUi(); };
        timer.Start();
        FormClosing += delegate
        {
            castToken = null; // el hilo de Cast cierra el receptor en la TV
            server.Stop();
            if (castThread != null) castThread.Join(1500);
        };
        Shown += delegate { ScanTvs(); if (autostart) Toggle(); };
    }

    static void Background(ThreadStart f)
    {
        Thread t = new Thread(f);
        t.IsBackground = true;
        t.Start();
    }

    void ScanTvs()
    {
        btnScan.Enabled = false;
        btnScan.Text = "Buscando...";
        Background(delegate
        {
            List<KeyValuePair<string, string>> found;
            try { found = Cast.Discover(); } catch { found = new List<KeyValuePair<string, string>>(); }
            try { BeginInvoke((MethodInvoker)delegate { FillTvs(found); }); } catch { }
        });
    }

    void FillTvs(List<KeyValuePair<string, string>> found)
    {
        int i = cmbTv.SelectedIndex;
        string prev = (i > 0 && i < tvIps.Count) ? tvIps[i] : null;
        cmbTv.Items.Clear();
        tvIps = new List<string>();
        cmbTv.Items.Add(found.Count == 0 ? "Ninguna TV encontrada (solo dirección manual)" : "Ninguna (solo dirección manual)");
        tvIps.Add(null);
        foreach (KeyValuePair<string, string> kv in found)
        {
            cmbTv.Items.Add(kv.Key + "   (" + kv.Value + ")");
            tvIps.Add(kv.Value);
        }
        int sel = prev != null ? tvIps.IndexOf(prev) : -1;
        cmbTv.SelectedIndex = sel > 0 ? sel : (tvIps.Count > 1 ? 1 : 0);
        btnScan.Enabled = true;
        btnScan.Text = "Buscar TVs";
        // Si se pulso Iniciar mientras se buscaban TVs, enviar ahora a la TV encontrada.
        if (castPending && server.Running && castToken == null && cmbTv.SelectedIndex > 0) StartCast();
        castPending = false;
        UpdateUi();
    }

    volatile object castToken;
    Thread castThread;
    bool castPending;

    void StartCast()
    {
        int i = cmbTv.SelectedIndex;
        string ip = (i > 0 && i < tvIps.Count) ? tvIps[i] : null;
        if (ip == null) return;
        int port = (int)numPort.Value;
        object token = new object();
        castToken = token;
        castedIp = ip;
        castMsg = "Conectando con la TV...";
        castThread = new Thread(delegate()
        {
            string err = null;
            try
            {
                Cast.Launch(ip, port,
                    delegate { return castToken == token; },
                    delegate(string m) { try { BeginInvoke((MethodInvoker)delegate { if (castToken == token) { castMsg = m; UpdateUi(); } }); } catch { } });
            }
            catch (Exception ex) { err = ex.Message; }
            if (castToken != token) return; // detenido por el usuario
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (castToken != token) return;
                    castToken = null; castedIp = null;
                    castMsg = "No se pudo enviar a la TV";
                    UpdateUi();
                    MessageBox.Show(this, "No se pudo enviar a la TV:\n" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            }
            catch { }
        });
        castThread.IsBackground = true;
        castThread.Start();
    }

    void AddLabel(string text, int x, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = true;
        l.Location = new Point(x, y);
        host.Controls.Add(l);
    }

    void LoadScreens()
    {
        int prev = cmbScreen.SelectedIndex;
        screens = Screen.AllScreens;
        cmbScreen.Items.Clear();
        for (int i = 0; i < screens.Length; i++)
        {
            Rectangle b = screens[i].Bounds;
            cmbScreen.Items.Add("Pantalla " + (i + 1) + "  —  " + b.Width + " x " + b.Height +
                                (screens[i].Primary ? "  (principal)" : ""));
        }
        cmbScreen.SelectedIndex = (prev >= 0 && prev < screens.Length) ? prev : 0;
    }

    void ApplyScreen()
    {
        int i = cmbScreen.SelectedIndex;
        if (i >= 0 && i < screens.Length) server.SetBounds(screens[i].Bounds);
        if (server.Running) server.RestartLive();
    }

    void ApplyQuality()
    {
        int q = Math.Max(0, cmbQuality.SelectedIndex);
        server.Quality = QualityJpeg[q];
        server.MaxHeight = QualityMaxH[q];
        server.Bitrate = QualityBitrate[q];
        server.Fps = FpsValues[Math.Max(0, cmbFps.SelectedIndex)];
        if (server.Running) server.RestartLive();
    }

    void Toggle()
    {
        if (server.Running)
        {
            castToken = null; // el hilo de Cast cierra el receptor y la TV vuelve a su inicio
            castedIp = null; castMsg = null; castPending = false;
            server.Stop();
        }
        else
        {
            int port = (int)numPort.Value;
            try { ApplyScreen(); server.Start(port); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo iniciar en el puerto " + port + ":\n" + ex.Message +
                                "\n\nPrueba con otro puerto.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateUi();
                return;
            }
            if (!btnScan.Enabled) { castPending = true; castMsg = "Buscando la TV..."; } // se enviara al terminar la busqueda
            else StartCast();
        }
        UpdateUi();
    }

    void UpdateUi()
    {
        bool on = server.Running;
        btnStart.Text = on ? "■  Detener transmisión" : "▶  Iniciar transmisión";
        numPort.Enabled = !on;
        List<string> ips = LocalIps();
        string port = ":" + numPort.Value;
        string url = ips.Count > 0 ? "http://" + ips[0] + port : "(sin red)";
        if (txtUrl.Text != url) txtUrl.Text = url;
        string other = "";
        for (int i = 1; i < ips.Count; i++) other += (i > 1 ? ",  " : "Otras direcciones de este PC:  ") + ips[i] + port;
        lblOther.Text = other;
        if (!on) { lblStatus.ForeColor = SystemColors.GrayText; lblStatus.Text = "Detenido"; }
        else
        {
            int n = server.StreamClients;
            lblStatus.ForeColor = n > 0 ? Color.Green : Color.DarkOrange;
            lblStatus.Text = n > 0 ? "Transmitiendo  —  " + n + " dispositivo(s) conectado(s)" : (castMsg ?? "Esperando a que un dispositivo se conecte...");
        }
    }

    static readonly List<string> gatewayIps = new List<string>();
    internal static bool HasGateway(string ip) { lock (gatewayIps) return gatewayIps.Contains(ip); }

    internal static List<string> LocalIps()
    {
        List<string> withGw = new List<string>(), rest = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties p = ni.GetIPProperties();
                bool gw = false;
                foreach (GatewayIPAddressInformation g in p.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) gw = true;
                foreach (UnicastIPAddressInformation a in p.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = a.Address.ToString();
                    if (s.StartsWith("169.254.")) continue;
                    (gw ? withGw : rest).Add(s);
                }
            }
        }
        catch { }
        lock (gatewayIps) { gatewayIps.Clear(); gatewayIps.AddRange(withGw); }
        withGw.AddRange(rest);
        return withGw;
    }
}
