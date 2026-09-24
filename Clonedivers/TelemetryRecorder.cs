using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Clonedivers;

/// <summary>Observes Windows counters and external ETW output; never reads game process memory.</summary>
public sealed class TelemetryRecorder : IDisposable
{
    readonly TelemetrySettings settings;
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentQueue<TelemetryStore.Row> events = new();
    readonly object gate = new();
    readonly List<TelemetryStore.Row> rows = new();
    DateTimeOffset lastOperation;
    readonly Task loop;
    string session = Guid.NewGuid().ToString("N");
    int rowBytes;
    SessionLog.Context selection;
    public string Status { get; private set; } = "Waiting for HD2";
    public string UploadStatus { get; private set; } = "Reports waiting locally";
    public TelemetryRecorder(TelemetrySettings settings, SessionLog.Context selection)
    {
        this.settings = settings; this.selection = selection;
        loop = Task.Run(Run);
    }
    public void UpdateContext(SessionLog.Context value) => selection = value;
    public void Event(string kind, double? seconds = null)
    {
        if(events.Count < 500)events.Enqueue(new("event",DateTimeOffset.UtcNow,new(){["event"]=kind,["seconds"]=seconds}));
    }
    void Add(TelemetryStore.Row row)
    {
        lock(gate) {
            var size=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row,TelemetryStore.Json).Length+1;
            if(rowBytes+size>400*1024||rows.Count>=1000)Flush();
            rows.Add(row);rowBytes+=size;
        }
    }
    void Flush()
    {
        lock(gate) {
            if(rows.Count==0)return;
            try { TelemetryStore.Save(new(1,settings.DeviceId,session,Guid.NewGuid().ToString("N"),settings.Nickname,rows.ToArray())); }
            catch { Status="Local report storage unavailable"; }
            rows.Clear();rowBytes=0;
        }
    }
    async Task Run()
    {
        Task upload=Task.CompletedTask; var nextUpload=DateTimeOffset.MinValue;int failures=0;
        Process? game=null;TelemetryMachine? machine=null;Task? frames=null;CancellationTokenSource? frameStop=null;
        var since=DateTimeOffset.UtcNow;var lastFlush=Stopwatch.StartNew();var lastContext=Stopwatch.StartNew();
        async Task EndGame(string reason) {
            if(game is null)return;
            frameStop?.Cancel(); if(frames is not null)try{await frames;}catch{}
            Add(new("event",DateTimeOffset.UtcNow,new(){["event"]=reason,["exitCause"]="undetermined"}));
            if(!stop.IsCancellationRequested) {
                try { foreach(var row in TelemetryMachine.CrashFiles(since))Add(row);foreach(var row in await TelemetryMachine.CrashEvents(since,stop.Token))Add(row); } catch { Event("crash-diagnostics-unavailable"); }
            }
            Flush();game.Dispose();game=null;machine?.Dispose();machine=null;frameStop?.Dispose();frameStop=null;frames=null;
        }
        try {
            while(!stop.IsCancellationRequested && settings.Enabled) {
                try {
                    if(game is not null && game.HasExited)await EndGame("game-exit-observed");
                    if(game is null) {
                        var candidates=Process.GetProcessesByName("helldivers2");game=candidates.FirstOrDefault();foreach(var extra in candidates.Skip(1))extra.Dispose();
                        if(game is not null) {
                            Flush();session=Guid.NewGuid().ToString("N");since=DateTimeOffset.UtcNow;
                            machine=new(game.Id);Add(new("context",since,TelemetryMachine.Context(selection)));
                            Add(new("event",since,new(){["event"]="game-observed-running",["qpcMilliseconds"]=Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency}));
                            frameStop=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                            frames=CaptureFrames(game.Id,frameStop.Token);lastContext.Restart();
                        }
                    }
                    while(events.TryDequeue(out var row))Add(row);
                    if(game is not null) {
                        Add(new("resource",DateTimeOffset.UtcNow,machine!.Sample(game)));
                        if(lastContext.Elapsed.TotalMinutes>=5){Add(new("context",DateTimeOffset.UtcNow,TelemetryMachine.Context(selection)));lastContext.Restart();}
                    }
                    if(lastFlush.Elapsed.TotalSeconds>=20){
                        foreach(var operation in SupportHistory.Read().Where(e=>e.Utc>lastOperation)) {
                            Add(new("event",DateTimeOffset.UtcNow,new(){["event"]="pack-"+operation.Event,["operationUtc"]=operation.Utc.ToString("O"),["pack"]=operation.Pack,["profile"]=operation.Profile,["errorCategory"]=operation.ErrorCategory}));
                            if(operation.Utc>lastOperation)lastOperation=operation.Utc;
                        }
                        Flush();lastFlush.Restart();
                    }
                    if(upload.IsCompleted && DateTimeOffset.UtcNow>=nextUpload) {
                        upload=Task.Run(async()=>{
                            try{var sent=await TelemetryStore.Upload(settings,stop.Token);failures=0;UploadStatus=sent>0?$"Uploaded {sent} report chunks at {DateTime.Now:t}":"Upload queue checked";}
                            catch(OperationCanceledException){}
                            catch{failures=Math.Min(failures+1,6);UploadStatus="Upload unavailable; reports queued locally";}
                            nextUpload=DateTimeOffset.UtcNow.AddSeconds(30*Math.Pow(2,failures));
                        });
                    }
                } catch { Status="Partial capture; some observations unavailable"; }
                await Task.Delay(2000,stop.Token);
            }
        } catch(OperationCanceledException){}
        finally {
            await EndGame("monitor-stopped");
            while(events.TryDequeue(out var row))Add(row);
            Flush();try{await upload;}catch{}
        }
    }
    static string ExtractPresentMon()
    {
        var folder=Path.Combine(TelemetryStore.Root,"tools");FileSafety.RejectLink(TelemetryStore.Root);FileSafety.RejectLink(folder);Directory.CreateDirectory(folder);
        var path=FileSafety.Child(folder,"PresentMon-2.5.1.exe");
        using(var license=Assembly.GetExecutingAssembly().GetManifestResourceStream("Clonedivers.PresentMon.LICENSE.txt")) {
            if(license is not null){using var output=File.Create(FileSafety.Child(folder,"PresentMon.LICENSE.txt"));license.CopyTo(output);}
        }
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("Clonedivers.PresentMon.exe") ?? throw new FileNotFoundException("Bundled PresentMon missing");
        var expected=SHA256.HashData(resource);resource.Position=0;
        if(File.Exists(path)){using var old=File.OpenRead(path);if(SHA256.HashData(old).SequenceEqual(expected))return path;}
        FileSafety.RejectLink(path+".tmp");using(var output=File.Create(path+".tmp"))resource.CopyTo(output);File.Move(path+".tmp",path,true);return path;
    }
    async Task CaptureFrames(int pid,CancellationToken cancel)
    {
        string executable;
        try{executable=ExtractPresentMon();}catch{Status="FPS unavailable: recorder missing";Event("frame-recorder-missing");return;}
        var name="Clonedivers-"+settings.DeviceId;
        while(!cancel.IsCancellationRequested) {
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"--process_id",pid.ToString(),"--session_name",name,"--stop_existing_session","--output_stdout","--no_console_stats","--no_track_input","--v2_metrics","--qpc_time_ms","--timed","3600","--terminate_after_timed","--terminate_on_proc_exit"})info.ArgumentList.Add(arg);
            using var recorder=Process.Start(info);if(recorder is null){Event("frame-recorder-start-failed");return;}
            using var registration=cancel.Register(()=>{try{if(!recorder.HasExited)recorder.Kill();}catch{}});
            var stderr=Task.Run(async()=>{while(await recorder.StandardError.ReadLineAsync() is {} line){/* Drain, never upload arbitrary process output. */}});
            try {
                string[]? header=null;var observed=false;
                while(await recorder.StandardOutput.ReadLineAsync(cancel) is {} line) {
                    if(line.Length>16000)continue;
                    if(header is null){if(line.StartsWith("Application,",StringComparison.Ordinal))header=line.Split(',');continue;}
                    var fields=TelemetryStore.ParseFrame(header,line);if(fields.Count==0)continue;
                    if(!observed){Event("frame-capture-started");Status="Recording frames and resource use";observed=true;}
                    Add(new("frame",DateTimeOffset.UtcNow,fields));
                }
                await recorder.WaitForExitAsync(cancel);await stderr;
                Event("frame-recorder-exit",recorder.ExitCode);
                if(recorder.ExitCode!=0){Status="FPS unavailable: tracing permission or recorder error";return;}
                if(!observed){Status="No frame events observed";await Task.Delay(5000,cancel);}
            } catch(OperationCanceledException){} catch {Status="Frame capture interrupted";Event("frame-capture-interrupted");return;}
            finally {
                try {
                    if(!recorder.HasExited)recorder.Kill();await recorder.WaitForExitAsync();await stderr;
                    var cleanup=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
                    foreach(var arg in new[]{"--session_name",name,"--terminate_existing_session","--no_console_stats"})cleanup.ArgumentList.Add(arg);
                    using var child=Process.Start(cleanup);if(child is not null && !child.WaitForExit(2000))child.Kill();
                }catch{}
            }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        try{loop.Wait(TimeSpan.FromSeconds(5));}catch{}
        // A cancelled upload cannot acknowledge/delete queued files after the next enabled run.
    }
}
