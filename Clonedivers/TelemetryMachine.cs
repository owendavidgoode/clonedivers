using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clonedivers;

public sealed class TelemetryMachine : IDisposable
{
    long lastClock; double? lastCpu;
    ulong? lastKernel, lastUser, lastIdle;
    IntPtr query;
    readonly Dictionary<string, IntPtr> counters = new();
    public TelemetryMachine(int pid)
    {
        try
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out query) != 0) return;
            foreach (var (name, path) in new[] {
                ("gpuEngine", $@"\GPU Engine(pid_{pid}_*)\Utilization Percentage"),
                ("gpuDedicatedBytes", $@"\GPU Process Memory(pid_{pid}_*)\Dedicated Usage"),
                ("gpuSharedBytes", $@"\GPU Process Memory(pid_{pid}_*)\Shared Usage"),
                ("systemPagesInputPerSec", @"\Memory\Pages Input/sec"),
                ("systemPagesOutputPerSec", @"\Memory\Pages Output/sec"),
                ("systemDiskReadBytesPerSec", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec"),
                ("systemDiskWriteBytesPerSec", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec"),
                ("systemDiskQueue", @"\PhysicalDisk(_Total)\Current Disk Queue Length") })
                if (PdhAddEnglishCounter(query, path, UIntPtr.Zero, out var handle) == 0) counters[name] = handle;
            PdhCollectQueryData(query);
        }
        catch { Dispose(); }
    }
    public Dictionary<string, object?> Sample(Process process)
    {
        var f = new Dictionary<string, object?>(); process.Refresh(); var clock = Stopwatch.GetTimestamp();
        f["elapsedClockSeconds"] = clock / (double)Stopwatch.Frequency;
        GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
        f["foreground"] = foreground == process.Id;
        Try(() => {
            var cpu = process.TotalProcessorTime.TotalSeconds;
            f["gameCpuPercentAllCores"] = lastCpu is double previous && clock > lastClock ? Math.Clamp(100 * (cpu - previous) * Stopwatch.Frequency / (clock - lastClock) / Environment.ProcessorCount, 0, 100) : null;
            lastCpu = cpu;
        }); lastClock = clock;
        Try(() => { f["workingSetBytes"] = process.WorkingSet64; f["privateBytes"] = process.PrivateMemorySize64; f["handles"] = process.HandleCount; f["threads"] = process.Threads.Count; });
        Try(() => { if (GetProcessIoCounters(process.Handle, out var io)) { f["readBytesCumulative"] = io.ReadTransfer; f["writeBytesCumulative"] = io.WriteTransfer; f["otherBytesCumulative"] = io.OtherTransfer; } });
        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem)) { f["systemAvailableRamBytes"] = mem.AvailablePhysical; f["systemRamLoadPercent"] = mem.Load; }
        var perf = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
        if (GetPerformanceInfo(ref perf, perf.Size)) { f["systemCommittedBytes"] = perf.CommitTotal.ToUInt64() * perf.PageSize.ToUInt64(); f["systemCommitLimitBytes"] = perf.CommitLimit.ToUInt64() * perf.PageSize.ToUInt64(); }
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (lastKernel is ulong k && lastUser is ulong u && lastIdle is ulong i && kernel >= k && user >= u && idle >= i)
            { var total = (kernel-k)+(user-u); f["systemCpuPercent"] = total > 0 ? Math.Clamp(100d * (total - Math.Min(total,idle-i)) / total,0,100) : null; }
            lastKernel=kernel;lastUser=user;lastIdle=idle;
        }
        Try(() => {
            if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return;
            foreach (var (name, counter) in counters)
            {
                uint bytes=0,count=0; var result = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, ref count, IntPtr.Zero);
                if (result != 0x800007D2 || bytes == 0 || bytes > 1024*1024) { f[name]=null; continue; }
                var buffer=Marshal.AllocHGlobal((int)bytes);
                try {
                    if (PdhGetFormattedCounterArray(counter,0x200,ref bytes,ref count,buffer)!=0) { f[name]=null; continue; }
                    var valid = new List<double>();
                    for (int n=0;n<Math.Min(count,128);n++) {
                        var item=Marshal.PtrToStructure<CounterItem>(buffer+n*Marshal.SizeOf<CounterItem>());
                        if (item.Value.Status>1 || !double.IsFinite(item.Value.Value)) continue;
                        valid.Add(item.Value.Value);
                        if (name=="gpuEngine") {
                            var instance=Marshal.PtrToStringUni(item.Name) ?? "";
                            var type=Regex.Match(instance,@"eng_(\d+)_engtype_([A-Za-z0-9]+)");
                            if(type.Success) f[$"gpuEngine.{n}.{type.Groups[2].Value}"]=item.Value.Value;
                        }
                    }
                    f[name]=valid.Count==0?null:name=="gpuEngine"?valid.Max():valid.Sum();
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        });
        return f;
    }
    static void Try(Action action) { try { action(); } catch { } }
    public void Dispose() { if(query!=IntPtr.Zero){ PdhCloseQuery(query);query=IntPtr.Zero;} }
    public static Dictionary<string, object?> Context(SessionLog.Context selection)
    {
        var h=Diagnostics.ReadHardware();
        var f=new Dictionary<string,object?> { ["cpu"]=h.Cpu,["logicalProcessors"]=h.LogicalProcessors,["ramBytes"]=h.PhysicalBytes,["windows"]=h.Windows,["desktopDisplays"]=string.Join(",",h.Displays),["pack"]=selection.Pack,["gameBuild"]=selection.GameBuild,["profile"]=selection.Profile,["mode"]=selection.Mode,["delta"]=selection.Delta,["droids"]=selection.Droids,["aimPoints"]=selection.AimPoints,["launcher"]=AppInfo.Version,["presentMon"]="2.5.1",["sampleSeconds"]=2,["gpuMemoryCaveat"]="Windows PDH estimates; shared allocations may be counted more than once",["frameClock"]="CPUStartQPC milliseconds; record UTC is receive time" };
        for(int n=0;n<Math.Min(8,h.Adapters.Count);n++){f[$"gpu{n}"]=h.Adapters[n].Name;f[$"gpu{n}CapacityBytes"]=h.Adapters[n].DedicatedBytes;}
        Try(() => {
            using var display=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach(var name in display?.GetSubKeyNames().Where(n=>Regex.IsMatch(n,@"^\d{4}$")).Take(8)??Enumerable.Empty<string>()) {
                using var adapter=display!.OpenSubKey(name);
                f["driver"+name]=TelemetryStore.SafeText(adapter?.GetValue("DriverDesc") as string,160);
                f["driverVersion"+name]=TelemetryStore.SafeText(adapter?.GetValue("DriverVersion") as string,80);
                f["driverDate"+name]=TelemetryStore.SafeText(adapter?.GetValue("DriverDate") as string,80);
            }
        });
        Try(() => {
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Arrowhead","Helldivers2","user_settings.config");
            if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)return;
            var text=File.ReadAllText(path);
            foreach(var key in new[]{"screen_resolution","render_resolution","render_scale","render_resolution_factor_index","upscaling_method","upscaling_quality","texture_quality","vertical_fov","vsync","max_fps","framerate_limit","framerate_limit_enabled","fullscreen","object_lod_quality","terrain_quality","particle_quality","shadows","volumetric_clouds_quality","volumetric_fog_quality","reflection_quality","lighting_and_material_quality"}) {
                var match=Regex.Match(text,@"(?m)^\s*"+key+@"\s*=\s*([0-9.\s\[\],-]+|true|false)\s*$");
                f["graphics."+key]=match.Success?TelemetryStore.SafeText(Regex.Replace(match.Groups[1].Value,@"\s+"," "),120):null;
            }
        });
        return f;
    }
    public static IEnumerable<TelemetryStore.Row> CrashFiles(DateTimeOffset since)
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Arrowhead","Helldivers2","dumps");
        if(!Directory.Exists(path))yield break;
        foreach(var file in new DirectoryInfo(path).GetFiles().Where(f=>f.LastWriteTimeUtc>=since.UtcDateTime).Take(30))
            if(file.Extension is ".dmp" or ".txt")yield return new("crash",file.LastWriteTimeUtc,new(){["source"]="HD2 dump directory",["type"]=file.Extension,["bytes"]=file.Length,["contentsUploaded"]=false});
    }
    public static async Task<IReadOnlyList<TelemetryStore.Row>> CrashEvents(DateTimeOffset since, CancellationToken cancel)
    {
        var result=new List<TelemetryStore.Row>();
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"wevtutil.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"qe","Application","/q:*[System[(EventID=1000 or EventID=1001 or EventID=1002) and TimeCreated[@SystemTime >= '"+since.UtcDateTime.ToString("O",CultureInfo.InvariantCulture)+"']]]","/f:xml","/c:40","/rd:true"})start.ArgumentList.Add(arg);
        using var p=Process.Start(start); if(p is null)return result;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try {
            var errors=p.StandardError.ReadToEndAsync(timeout.Token);
            var buffer=new char[4096];var text=new System.Text.StringBuilder();int read;
            while((read=await p.StandardOutput.ReadAsync(buffer,timeout.Token))>0){if(text.Length+read>512*1024)break;text.Append(buffer,0,read);}
            if(!p.HasExited && text.Length>=508*1024)p.Kill();
            await p.WaitForExitAsync(timeout.Token);await errors;
            var doc=XDocument.Parse("<Events>"+text+"</Events>");
            foreach(var e in doc.Descendants().Where(x=>x.Name.LocalName=="Event")) {
                var data=e.Descendants().Where(x=>x.Name.LocalName=="Data").ToArray();
                if(!data.Any(x=>x.Value.Equals("helldivers2.exe",StringComparison.OrdinalIgnoreCase)))continue;
                var fields=new Dictionary<string,object?>{["source"]="Windows Application",["eventId"]=e.Descendants().FirstOrDefault(x=>x.Name.LocalName=="EventID")?.Value};
                foreach(var d in data){var name=d.Attribute("Name")?.Value;if(name is "AppName" or "AppVersion" or "ModuleName" or "ModuleVersion" or "ExceptionCode" or "FaultingOffset")fields[name]=TelemetryStore.SafeText(Path.GetFileName(d.Value),200);}
                var time=e.Descendants().FirstOrDefault(x=>x.Name.LocalName=="TimeCreated")?.Attribute("SystemTime")?.Value;
                result.Add(new("crash",DateTimeOffset.TryParse(time,out var utc)?utc:DateTimeOffset.UtcNow,fields));
            }
        } finally { if(!p.HasExited)p.Kill(); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations,WriteOperations,OtherOperations,ReadTransfer,WriteTransfer,OtherTransfer; }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtended; }
    [StructLayout(LayoutKind.Sequential)] struct PerformanceInfo { public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;public uint Handles,Processes,Threads; }
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhOpenQueryW")] static extern uint PdhOpenQuery(string? source,UIntPtr data,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")] static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhGetFormattedCounterArrayW")] static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,ref uint count,IntPtr buffer);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process,out IoCounters counters);
    [DllImport("psapi.dll")] static extern bool GetPerformanceInfo(ref PerformanceInfo info,uint size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
}
