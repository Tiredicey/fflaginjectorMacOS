using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace FlagInjector;

static class Mach
{
    const string Lib = "libSystem.B.dylib";
    public const int KERN_SUCCESS = 0;
    public const int VM_REGION_BASIC_INFO_64 = 9;
    public const int VM_PROT_READ = 1;
    public const int VM_PROT_WRITE = 2;
    public const int VM_PROT_EXECUTE = 4;
    public const int VM_PROT_COPY = 16;
    public const uint MH_MAGIC_64 = 0xFEEDFACF;
    public const uint MH_EXECUTE = 2;
    [DllImport(Lib)] public static extern uint task_self_trap();
    [DllImport(Lib)] public static extern int task_for_pid(uint target, int pid, out uint task);
    [DllImport(Lib)] public static extern int mach_port_deallocate(uint task, uint name);
    [DllImport(Lib)] public static extern int mach_vm_read_overwrite(uint task, ulong addr, ulong size, IntPtr data, out ulong outsize);
    [DllImport(Lib)] public static extern int mach_vm_write(uint task, ulong addr, IntPtr data, uint count);
    [DllImport(Lib)] public static extern int mach_vm_protect(uint task, ulong addr, ulong size, int setMax, int prot);
    [DllImport(Lib)] public static extern int mach_vm_region(uint task, ref ulong addr, ref ulong size, int flavor, IntPtr info, ref uint infoCnt, out uint objName);
    [DllImport(Lib)] public static extern int kill(int pid, int sig);
    [DllImport(Lib)] public static extern uint getuid();
}

enum FType { Bool, Int, Float, String }
enum ApplyMode { OnJoin, Immediate }

sealed class ThemeColors
{
    public Color Bg, Surface, Row2, Hover, Border, Fg, Sub, Accent, Green, Red, Peach, Yellow;
}

static class Th
{
    static readonly ThemeColors _dark = new()
    {
        Bg = Color.FromRgb(30, 30, 46), Surface = Color.FromRgb(36, 36, 51),
        Row2 = Color.FromRgb(41, 41, 58), Hover = Color.FromRgb(69, 71, 90),
        Border = Color.FromRgb(88, 91, 112), Fg = Color.FromRgb(205, 214, 244),
        Sub = Color.FromRgb(166, 173, 200), Accent = Color.FromRgb(137, 180, 250),
        Green = Color.FromRgb(166, 227, 161), Red = Color.FromRgb(243, 139, 168),
        Peach = Color.FromRgb(250, 179, 135), Yellow = Color.FromRgb(249, 226, 175)
    };
    static readonly ThemeColors _light = new()
    {
        Bg = Color.FromRgb(239, 241, 245), Surface = Color.FromRgb(230, 233, 239),
        Row2 = Color.FromRgb(220, 224, 232), Hover = Color.FromRgb(188, 192, 204),
        Border = Color.FromRgb(140, 143, 161), Fg = Color.FromRgb(35, 38, 52),
        Sub = Color.FromRgb(92, 95, 119), Accent = Color.FromRgb(30, 102, 245),
        Green = Color.FromRgb(64, 160, 43), Red = Color.FromRgb(210, 15, 57),
        Peach = Color.FromRgb(254, 100, 11), Yellow = Color.FromRgb(223, 142, 29)
    };
    public static ThemeColors C { get; private set; } = _dark;
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;
    public static void Toggle() { IsDark = !IsDark; C = IsDark ? _dark : _light; Changed?.Invoke(); }
    public static void Set(bool dark) { IsDark = dark; C = dark ? _dark : _light; Changed?.Invoke(); }
    public static IBrush B(Color c) => new SolidColorBrush(c);
}

sealed class AppLog : IDisposable
{
    readonly object _lk = new(); StreamWriter? _w;
    public AppLog(string dir)
    {
        var path = Path.Combine(dir, "log.txt");
        try
        {
            _w = new StreamWriter(path, true, Encoding.UTF8) { AutoFlush = true };
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
            { _w.Dispose(); File.Delete(path); _w = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true }; }
        }
        catch { _w = null; }
    }
    public void Info(string msg) => Write("INF", msg);
    public void Warn(string msg) => Write("WRN", msg);
    public void Error(string msg) => Write("ERR", msg);
    void Write(string lvl, string msg) { lock (_lk) { try { _w?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{lvl}] {msg}"); } catch { } } }
    public void Dispose() { lock (_lk) { _w?.Dispose(); _w = null; } }
}

sealed class AppSettings
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = 820;
    public int H { get; set; } = 780;
    public bool AutoApply { get; set; } = true;
    public bool Watchdog { get; set; } = true;
    public bool DarkTheme { get; set; } = true;
    public bool ConfirmApplyAll { get; set; }
    public string LastPreset { get; set; } = "";
    public List<string> SearchHistory { get; set; } = new();
    static readonly JsonSerializerOptions _jopt = new() { WriteIndented = true };
    public static AppSettings Load(string path)
    {
        try { if (!File.Exists(path)) return new(); return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new(); }
        catch { return new(); }
    }
    public void Save(string path)
    {
        try { var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, _jopt), new UTF8Encoding(false)); File.Move(tmp, path, true); } catch { }
    }
}

sealed class FlagHistoryEntry
{
    [JsonPropertyName("ts")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("ov")] public string OldValue { get; set; } = "";
    [JsonPropertyName("nv")] public string NewValue { get; set; } = "";
}

sealed class FlagDto
{
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("v")] public string Value { get; set; } = "";
    [JsonPropertyName("t")] public string Type { get; set; } = "String";
    [JsonPropertyName("e")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("m")] public string Mode { get; set; } = "OnJoin";
    [JsonPropertyName("h")] public List<FlagHistoryEntry> History { get; set; } = new();
}

sealed class NameResolver
{
    readonly Dictionary<string, string> _exact = new();
    readonly Dictionary<string, string> _ci = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _stripped = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _norm = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string canonical)
    {
        if (_exact.ContainsKey(canonical)) return;
        _exact[canonical] = canonical; _ci[canonical] = canonical;
        var s = FlagPrefix.Strip(canonical); if (!_stripped.ContainsKey(s)) _stripped[s] = canonical;
        var n = canonical.Replace("_", ""); if (!_norm.ContainsKey(n)) _norm[n] = canonical;
    }
    public string? Resolve(string name)
    {
        if (_exact.ContainsKey(name)) return name;
        if (_ci.TryGetValue(name, out var a)) return a;
        var s = FlagPrefix.Strip(name);
        if (s != name) { if (_ci.TryGetValue(s, out var b)) return b; if (_stripped.TryGetValue(s, out var c)) return c; }
        if (_stripped.TryGetValue(name, out var d)) return d;
        if (_norm.TryGetValue(name.Replace("_", ""), out var e)) return e;
        return null;
    }
    public void Clear() { _exact.Clear(); _ci.Clear(); _stripped.Clear(); _norm.Clear(); }
}

sealed class MemEngine : IDisposable
{
    uint _task; long _base; int _pid; uint _modSize; readonly object _lk = new();
    public bool On => _task != 0 && _base != 0;
    public int Pid { get { lock (_lk) return _pid; } }
    public long Base { get { lock (_lk) return _base; } }
    public uint ModSize { get { lock (_lk) return _modSize; } }
    public event Action<string>? Log;

    public bool Attach(int pid, string mod = "RobloxPlayer", CancellationToken ct = default)
    {
        lock (_lk)
        {
            Detach();
            var self = Mach.task_self_trap();
            int kr = Mach.task_for_pid(self, pid, out uint task);
            if (kr != Mach.KERN_SUCCESS) { Log?.Invoke($"task_for_pid err {kr}"); return false; }
            long b = 0; uint sz = 0;
            for (int i = 0; i < 40 && b == 0; i++)
            {
                if (ct.IsCancellationRequested) { Mach.mach_port_deallocate(self, task); return false; }
                FindMachOBase(task, out b, out sz);
                if (b == 0) Thread.Sleep(200);
            }
            if (b == 0) { Mach.mach_port_deallocate(self, task); Log?.Invoke("Base not found"); return false; }
            _task = task; _base = b; _pid = pid; _modSize = sz;
            Log?.Invoke($"Attached PID {pid} base 0x{b:X}"); return true;
        }
    }

    public void Detach()
    {
        lock (_lk)
        {
            if (_task != 0) Mach.mach_port_deallocate(Mach.task_self_trap(), _task);
            _task = 0; _base = 0; _pid = 0; _modSize = 0;
        }
    }

    public bool Alive()
    {
        bool entered = false;
        try { entered = Monitor.TryEnter(_lk, 50); if (!entered) return _task != 0; return _task != 0 && _pid != 0 && Mach.kill(_pid, 0) == 0; }
        finally { if (entered) Monitor.Exit(_lk); }
    }

    static void FindMachOBase(uint task, out long baseAddr, out uint modSize)
    {
        baseAddr = 0; modSize = 0; ulong addr = 0x100000000;
        while (addr < 0x800000000000)
        {
            ulong size = 0; var info = Marshal.AllocHGlobal(64); uint count = 16;
            try
            {
                int kr = Mach.mach_vm_region(task, ref addr, ref size, Mach.VM_REGION_BASIC_INFO_64, info, ref count, out _);
                if (kr != Mach.KERN_SUCCESS) break;
                int prot = Marshal.ReadInt32(info, 0);
                if ((prot & (Mach.VM_PROT_READ | Mach.VM_PROT_EXECUTE)) == (Mach.VM_PROT_READ | Mach.VM_PROT_EXECUTE))
                {
                    var hdr = new byte[32]; var pin = GCHandle.Alloc(hdr, GCHandleType.Pinned);
                    try
                    {
                        if (Mach.mach_vm_read_overwrite(task, addr, 32, pin.AddrOfPinnedObject(), out ulong outSz) == Mach.KERN_SUCCESS && outSz == 32)
                        {
                            uint magic = BitConverter.ToUInt32(hdr, 0); uint ft = BitConverter.ToUInt32(hdr, 12);
                            if (magic == Mach.MH_MAGIC_64 && ft == Mach.MH_EXECUTE) { baseAddr = (long)addr; modSize = (uint)Math.Min(size, uint.MaxValue); return; }
                        }
                    }
                    finally { pin.Free(); }
                }
            }
            finally { Marshal.FreeHGlobal(info); }
            addr += size; if (size == 0) break;
        }
    }

    public byte[]? ReadAbs(long addr, int n)
    {
        lock (_lk)
        {
            if (_task == 0) return null;
            var buf = new byte[n]; var pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { return Mach.mach_vm_read_overwrite(_task, (ulong)addr, (ulong)n, pin.AddrOfPinnedObject(), out ulong outSz) == Mach.KERN_SUCCESS && (int)outSz == n ? buf : null; }
            finally { pin.Free(); }
        }
    }

    public long ReadPtr(long addr) { var b = ReadAbs(addr, 8); return b != null ? BitConverter.ToInt64(b, 0) : 0; }
    public int ReadInt32(long addr) { var b = ReadAbs(addr, 4); return b != null ? BitConverter.ToInt32(b, 0) : 0; }

    public bool WriteFast(long addr, byte[] data)
    {
        lock (_lk)
        {
            if (_task == 0) return false;
            ulong ua = (ulong)addr; ulong ps = 16384;
            ulong pS = ua & ~(ps - 1); ulong pE = (ua + (ulong)data.Length + ps - 1) & ~(ps - 1);
            Mach.mach_vm_protect(_task, pS, pE - pS, 0, Mach.VM_PROT_READ | Mach.VM_PROT_WRITE | Mach.VM_PROT_COPY);
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return Mach.mach_vm_write(_task, ua, pin.AddrOfPinnedObject(), (uint)data.Length) == Mach.KERN_SUCCESS; }
            finally { pin.Free(); }
        }
    }

    public bool WriteAbs(long addr, byte[] data, int tries = 3, int delayMs = 20)
    {
        if (!On) return false;
        for (int t = 0; t < tries; t++)
        {
            if (t > 0) Thread.Sleep(delayMs);
            if (!WriteFast(addr, data)) continue;
            var rd = ReadAbs(addr, data.Length);
            if (rd != null && data.AsSpan().SequenceEqual(rd)) return true;
        }
        return false;
    }

    public int BatchWrite(List<(long addr, byte[] data)> ops)
    {
        if (!On) return 0; ops.Sort((a, b) => a.addr.CompareTo(b.addr));
        int ok = 0; foreach (var (addr, data) in ops) if (WriteFast(addr, data)) ok++;
        return ok;
    }

    public void Dispose() => Detach();
}

static class FlagPrefix
{
    static readonly string[] _prefixes = { "DFString", "SFString", "FString", "DFFlag", "SFFlag", "DFInt", "SFInt", "DFLog", "SFLog", "FFlag", "FInt", "FLog" };
    public static ReadOnlySpan<string> All => _prefixes;
    public static string Strip(string name)
    {
        foreach (var p in _prefixes)
            if (name.Length > p.Length && name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[p.Length]))
                return name[p.Length..];
        return name;
    }
}

static class FlagCategory
{
    static readonly (string[] keys, string cat)[] _rules =
    {
        (new[]{"Render","Graphics","Shader","Texture","Light","Shadow","Material","Mesh","Particle","PostEffect","MSAA","Antialias"}, "Rendering"),
        (new[]{"Physics","Simulation","Gravity","Collision","Velocity","Force"}, "Physics"),
        (new[]{"Network","Replicat","Packet","Latency","Bandwidth","Http","Ping"}, "Network"),
        (new[]{"Gui","Ui","Menu","Hud","Chat","TextBox","Button","Frame","Label","Scroll","TopBar","StarterGui"}, "UI"),
        (new[]{"Audio","Sound","Music","Volume"}, "Audio"),
        (new[]{"Fps","Perf","Throttle","Budget","Cache","Memory","GC","Pool","Batch","Queue","Thread"}, "Performance"),
        (new[]{"Debug","Log","Verbose","Trace","Assert","Diag","Telemetry","Analytics","Stat"}, "Debug"),
        (new[]{"Lua","Script","Module","Require","Bytecode","VM"}, "Scripting"),
        (new[]{"Camera","Zoom","Fov","ViewPort"}, "Camera"),
        (new[]{"Terrain","Water","Sky","Atmosphere","Cloud","Sun","Moon","Star"}, "Environment"),
        (new[]{"Anim","IK","Humanoid","Character","Avatar","R15","R6","Emote"}, "Character"),
        (new[]{"Place","Teleport","Game","Universe","Server","DataStore","DataModel"}, "Engine")
    };
    public static string Categorize(string flagName)
    {
        var s = FlagPrefix.Strip(flagName);
        foreach (var (keys, cat) in _rules)
            foreach (var k in keys)
                if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return cat;
        return "Other";
    }
}

sealed class FlogBank
{
    readonly MemEngine _mem; readonly NameResolver _resolver = new(); readonly object _lk = new();
    long _oPointer, _oToFlag = 0x30, _oToValue = 0xc0;
    long _oNodeLeft, _oNodeRight = 0x8, _oNodeParent = 0x10;
    long _oNodeValue = 0x20, _oPairKey, _oPairValue = 0x18;
    long _oStrFirstByte = 0, _oStrLongData = 0, _oStrLongSize = 0x8, _oStrLongCap = 0x10;
    long _oStrShortData = 0x1;
    long _oTreeBeginNode, _oTreeRoot = 0x8, _oTreeSize = 0x10;
    long _hashMapOff = 0x8;
    readonly Dictionary<string, long> _descMap = new(); readonly List<string> _names = new();
    public bool Ready { get; private set; }
    public int Count { get { lock (_lk) return _descMap.Count; } }
    public IReadOnlyList<string> Names { get { lock (_lk) return _names.ToArray(); } }
    public event Action<string>? Log;
    public FlogBank(MemEngine mem) => _mem = mem;

    public void ApplyOffsets(Dictionary<string, long> offsets)
    {
        foreach (var kv in offsets)
            switch (kv.Key)
            {
                case "Pointer": _oPointer = kv.Value; break;
                case "ToFlag": _oToFlag = kv.Value; break;
                case "ToValue": _oToValue = kv.Value; break;
                case "NodeLeft": case "NodeForward": _oNodeLeft = kv.Value; break;
                case "NodeRight": case "NodeBackward": _oNodeRight = kv.Value; break;
                case "NodeParent": _oNodeParent = kv.Value; break;
                case "NodeValue": case "NodePair": _oNodeValue = kv.Value; break;
                case "Key": _oPairKey = kv.Value; break;
                case "Value": _oPairValue = kv.Value; break;
                case "LongData": case "Data": _oStrLongData = kv.Value; break;
                case "LongSize": case "Size": _oStrLongSize = kv.Value; break;
                case "LongCap": case "Capacity": _oStrLongCap = kv.Value; break;
                case "ShortData": _oStrShortData = kv.Value; break;
                case "TreeBeginNode": case "FirstNode": _oTreeBeginNode = kv.Value; break;
                case "TreeRoot": _oTreeRoot = kv.Value; break;
                case "TreeSize": case "MapSize": _oTreeSize = kv.Value; break;
                case "HashMapOff": _hashMapOff = kv.Value; break;
            }
    }

    public bool Init()
    {
        lock (_lk)
        {
            _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false;
            if (!_mem.On) { Log?.Invoke("Bank: mem not attached"); return false; }
            if (_oPointer == 0) { Log?.Invoke("Bank: no pointer offset"); return false; }
            long singleton = _mem.ReadPtr(_mem.Base + _oPointer);
            if (singleton < 0x10000) { Log?.Invoke($"Bank: bad singleton 0x{singleton:X}"); return false; }
            long mapBase = singleton + _hashMapOff;
            long beginNode = _mem.ReadPtr(mapBase + _oTreeBeginNode);
            long root = _mem.ReadPtr(mapBase + _oTreeRoot);
            int mapSize = _mem.ReadInt32(mapBase + _oTreeSize);
            if (mapSize <= 0 || mapSize > 100000) { Log?.Invoke($"Bank: suspect mapSize {mapSize}"); return false; }
            if (root < 0x10000) { Log?.Invoke("Bank: bad root"); return false; }
            long sentinel = mapBase + _oTreeRoot - _oNodeLeft;
            var visited = new HashSet<long>(); int count = 0;
            long node = beginNode;
            int maxIter = Math.Min(mapSize + 200, 100000);
            for (int i = 0; i < maxIter && node != 0 && node != sentinel; i++)
            {
                if (!visited.Add(node)) break;
                long pairAddr = node + _oNodeValue;
                string? key = ReadLibcxxString(pairAddr + _oPairKey);
                if (key != null && key.Length > 0 && key.Length < 512)
                {
                    long descPtr = _mem.ReadPtr(pairAddr + _oPairValue);
                    if (descPtr > 0x10000 && !_descMap.ContainsKey(key))
                    { _descMap[key] = descPtr; _resolver.Add(key); _names.Add(key); count++; }
                }
                node = TreeNext(node, sentinel);
            }
            Ready = count > 0; Log?.Invoke($"Bank: {count} flags"); return Ready;
        }
    }

    long TreeNext(long node, long sentinel)
    {
        long right = _mem.ReadPtr(node + _oNodeRight);
        if (right != 0 && right != sentinel)
        {
            node = right;
            while (true) { long left = _mem.ReadPtr(node + _oNodeLeft); if (left == 0 || left == sentinel) break; node = left; }
            return node;
        }
        while (true)
        {
            long parent = _mem.ReadPtr(node + _oNodeParent);
            if (parent == 0 || parent == sentinel) return sentinel;
            if (node != _mem.ReadPtr(parent + _oNodeRight)) return parent;
            node = parent;
        }
    }

    string? ReadLibcxxString(long addr)
    {
        var raw = _mem.ReadAbs(addr, 24); if (raw == null) return null;
        byte first = raw[(int)_oStrFirstByte];
        bool isLong = (first & 1) != 0;
        if (!isLong)
        {
            int size = (first >> 1) & 0x7F;
            if (size < 0 || size > 22) return null; if (size == 0) return "";
            int off = (int)_oStrShortData;
            if (off + size > raw.Length) return null;
            var buf = new byte[size]; Array.Copy(raw, off, buf, 0, size);
            try { return Encoding.UTF8.GetString(buf); } catch { return null; }
        }
        long dataPtr = BitConverter.ToInt64(raw, (int)_oStrLongData);
        long longSize = BitConverter.ToInt64(raw, (int)_oStrLongSize);
        if (dataPtr < 0x10000 || longSize <= 0 || longSize > 4096) return null;
        var strBytes = _mem.ReadAbs(dataPtr, (int)longSize);
        if (strBytes == null) return null;
        try { return Encoding.UTF8.GetString(strBytes); } catch { return null; }
    }

    public string? Resolve(string name) { lock (_lk) return Ready ? _resolver.Resolve(name) : null; }
    public long GetValueAddr(string resolvedName) { lock (_lk) return _descMap.TryGetValue(resolvedName, out long desc) ? desc + _oToValue : 0; }
    public void Reset() { lock (_lk) { _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false; } }
}

sealed class OffsetStore
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Regex _rxUintptr = new(@"(?:inline\s+)?(?:constexpr\s+)?uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+);", RegexOptions.Compiled);
    static readonly Regex _rxNsFFlags = new(@"namespace\s+FFlags\s*\{([^}]+)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlList = new(@"namespace\s+FFlagList\s*\{([\s\S]*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex _rxNsFFlOff = new(@"namespace\s+FFlagOffsets\s*\{([\s\S]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    readonly Dictionary<string, long> _map = new(); readonly NameResolver _resolver = new();
    readonly HashSet<string> _seenStripped = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _names = new();
    public IReadOnlyDictionary<string, long> Map => _map;
    public IReadOnlyList<string> Names => _names;
    public int Count => _map.Count;
    public string Cache1 { get; set; } = "";
    public string Cache2 { get; set; } = "";
    public string Url1 { get; set; } = "https://imtheo.lol/Offsets/FFlags.hpp";
    public string Url2 { get; set; } = "https://npdrlaufeimrkvdnjijl.supabase.co/functions/v1/get-offsets";
    public long FlogPointer { get; private set; }
    public Dictionary<string, long> StructOffsets { get; } = new();
    public event Action<string>? Log;

    async Task<string?> FetchUrlAsync(string url, int retries, CancellationToken ct)
    {
        for (int i = 0; i <= retries; i++)
        {
            try { if (i > 0) await Task.Delay(500 * i, ct); return await _http.GetStringAsync(url, ct); }
            catch (OperationCanceledException) { throw; } catch { if (i == retries) return null; }
        }
        return null;
    }

    public bool Fetch(CancellationToken ct = default)
    {
        string? body1 = null, body2 = null; bool cached1 = false, cached2 = false;
        try
        {
            var t1 = FetchUrlAsync(Url1, 2, ct); var t2 = FetchUrlAsync(Url2, 2, ct);
            Task.WhenAll(t1, t2).GetAwaiter().GetResult(); body1 = t1.Result; body2 = t2.Result;
        }
        catch (OperationCanceledException) { return false; } catch (Exception ex) { Log?.Invoke("Net: " + ex.Message); }
        if (!string.IsNullOrEmpty(body1) && !string.IsNullOrEmpty(Cache1)) try { Directory.CreateDirectory(Path.GetDirectoryName(Cache1)!); File.WriteAllText(Cache1, body1, Encoding.UTF8); } catch { }
        if (!string.IsNullOrEmpty(body2) && !string.IsNullOrEmpty(Cache2)) try { Directory.CreateDirectory(Path.GetDirectoryName(Cache2)!); File.WriteAllText(Cache2, body2, Encoding.UTF8); } catch { }
        if (string.IsNullOrEmpty(body1) && File.Exists(Cache1)) try { body1 = File.ReadAllText(Cache1); cached1 = true; } catch { }
        if (string.IsNullOrEmpty(body2) && File.Exists(Cache2)) try { body2 = File.ReadAllText(Cache2); cached2 = true; } catch { }
        _map.Clear(); _resolver.Clear(); _seenStripped.Clear(); _names.Clear(); FlogPointer = 0; StructOffsets.Clear();
        int c1 = 0, c2 = 0;
        if (!string.IsNullOrEmpty(body1)) c1 = ParseSource1(body1);
        if (!string.IsNullOrEmpty(body2)) c2 = ParseSource2(body2);
        Log?.Invoke($"Src1:{c1}{(cached1 ? "(c)" : "")} Src2:{c2}{(cached2 ? "(c)" : "")} Total:{_map.Count}");
        return _map.Count > 0;
    }

    int ParseSource1(string body)
    {
        int count = 0; var ns = _rxNsFFlags.Match(body);
        string region = ns.Success ? ns.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        {
            if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
            if (v < 0x100000) continue; if (AddOffset(m.Groups[1].Value, v)) count++;
        }
        return count;
    }

    int ParseSource2(string body)
    {
        int count = 0; var flogNs = _rxNsFFlList.Match(body);
        if (flogNs.Success)
        {
            foreach (Match m in _rxUintptr.Matches(flogNs.Groups[1].Value))
            {
                if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
                string key = m.Groups[1].Value; if (key == "Pointer") FlogPointer = v; StructOffsets[key] = v;
            }
            body = body.Remove(flogNs.Index, flogNs.Length);
        }
        var outerNs = _rxNsFFlOff.Match(body);
        string region = outerNs.Success ? outerNs.Groups[1].Value : body;
        foreach (Match m in _rxUintptr.Matches(region))
        {
            if (!long.TryParse(m.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, null, out long v)) continue;
            string key = m.Groups[1].Value; if (StructOffsets.ContainsKey(key)) continue;
            if (v < 0x100000) continue; if (AddOffset(key, v)) count++;
        }
        return count;
    }

    bool AddOffset(string name, long offset)
    {         if (_map.ContainsKey(name)) return false;
        _map[name] = offset; _resolver.Add(name);
        var s = FlagPrefix.Strip(name); if (_seenStripped.Add(s)) _names.Add(name); return true;
    }
    public string? Resolve(string n) => _resolver.Resolve(n);
    public long Offset(string resolved) => _map.TryGetValue(resolved, out long v) ? v : -1;
}

sealed class FlagEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public FType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public ApplyMode Mode { get; set; } = ApplyMode.OnJoin;
    public List<FlagHistoryEntry> History { get; set; } = new();
    public volatile string Status = "";
    public DateTime AddedAt = DateTime.UtcNow;
    byte[]? _cachedBytes; string? _cachedValue;

    public byte[] GetBytes()
    {
        var v = Value; if (_cachedValue == v && _cachedBytes != null) return _cachedBytes;
        var result = Type switch
        {
            FType.Bool => new[] { (byte)(v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? 1 : 0) },
            FType.Int => BitConverter.GetBytes(int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv) ? iv : 0),
            FType.Float => BitConverter.GetBytes(float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv) ? fv : 0f),
            _ => Encoding.UTF8.GetBytes(v + '\0')
        };
        _cachedBytes = result; _cachedValue = v; return result;
    }
    public void InvalidateCache() { _cachedBytes = null; _cachedValue = null; }
    public void RecordChange(string oldVal, string newVal)
    {
        History.Add(new FlagHistoryEntry { Timestamp = DateTime.UtcNow.ToString("o"), OldValue = oldVal, NewValue = newVal });
        if (History.Count > 20) History.RemoveAt(0);
    }
    public static FType InferFromName(string name)
    {
        foreach (var p in FlagPrefix.All)
            if (name.Length > p.Length && name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[p.Length]))
            {
                if (p.Contains("Flag", StringComparison.OrdinalIgnoreCase)) return FType.Bool;
                if (p.Contains("Int", StringComparison.OrdinalIgnoreCase) || p.Contains("Log", StringComparison.OrdinalIgnoreCase)) return FType.Int;
                if (p.Contains("String", StringComparison.OrdinalIgnoreCase)) return FType.String;
            }
        return FType.String;
    }
    public static FType InferFromValue(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return FType.String;
        var lv = v.Trim().ToLowerInvariant();
        if (lv is "true" or "false") return FType.Bool;
        if (int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return FType.Int;
        if (float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _) && v.Contains('.')) return FType.Float;
        return FType.String;
    }
    public static FType Infer(string name, string value) { var fn = InferFromName(name); return fn != FType.String ? fn : InferFromValue(value); }
}

sealed class UndoStack
{
    const int MaxDepth = 50;
    readonly List<FlagSnapshot[]> _undo = new(), _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Push(List<FlagEntry> flags) { _undo.Add(Snap(flags)); if (_undo.Count > MaxDepth) _undo.RemoveAt(0); _redo.Clear(); }
    public FlagSnapshot[]? Undo(List<FlagEntry> current) { if (_undo.Count == 0) return null; _redo.Add(Snap(current)); var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); return s; }
    public FlagSnapshot[]? Redo(List<FlagEntry> current) { if (_redo.Count == 0) return null; _undo.Add(Snap(current)); var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); return s; }
    static FlagSnapshot[] Snap(List<FlagEntry> flags) => flags.Select(f => new FlagSnapshot(f.Name, f.Value, f.Type, f.Enabled, f.Mode, f.History.ToList())).ToArray();
    public record FlagSnapshot(string Name, string Value, FType Type, bool Enabled, ApplyMode Mode, List<FlagHistoryEntry> History);
}

sealed class PresetManager
{
    readonly string _dir;
    public PresetManager(string dir) { _dir = Path.Combine(dir, "presets"); Directory.CreateDirectory(_dir); }
    public string[] List()
    {
        try { return Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Select(n => n!).OrderBy(n => n).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
    public void Save(string name, List<FlagEntry> flags)
    {
        var dtos = flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History }).ToArray();
        File.WriteAllText(Path.Combine(_dir, name + ".json"), JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
    public FlagDto[]? Load(string name)
    {
        var p = Path.Combine(_dir, name + ".json"); if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<FlagDto[]>(File.ReadAllText(p, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { return null; }
    }
    public void Delete(string name) { try { File.Delete(Path.Combine(_dir, name + ".json")); } catch { } }
}
