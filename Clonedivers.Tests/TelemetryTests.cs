using System.Text.Json;

namespace Clonedivers.Tests;

static class TelemetryTests
{
    public static void Run(string root,Action<bool,string> check)
    {
        check(TelemetryStore.ValidEndpoint("https://collector.example/") && !TelemetryStore.ValidEndpoint("http://collector.example/") && !TelemetryStore.ValidEndpoint("https://user:password@collector.example/") && !TelemetryStore.ValidEndpoint("https://collector.example/?secret=yes"),"telemetry only sends credentials to a plain HTTPS origin");
        var fields=TelemetryStore.ParseFrame(new[]{"Application","ProcessID","SwapChainAddress","CPUFrameTime","GPUBusy","Runtime","PresentMode"},"helldivers2.exe,42,0xabcd,16.5,NaN,DXGI,Hardware: Independent Flip");
        check(!fields.ContainsKey("Application")&&!fields.ContainsKey("ProcessID") && fields["SwapChainAddress"] is string && fields["CPUFrameTime"] is double ms && ms==16.5 && fields["GPUBusy"] is null,"frame parser retains timing and swapchain while dropping identity fields and invalid numbers");
        check(TelemetryStore.ParseFrame(new[]{"CPUFrameTime"},"1,2").Count==0,"malformed frame rows are rejected");
        var folder=Path.Combine(root,"telemetry");
        var chunk=new TelemetryStore.Chunk(1,new('a',32),new('b',32),new('c',32),"Tester",new[]{new TelemetryStore.Row("event",DateTimeOffset.UtcNow,new(){["event"]="game-exit-observed"})});
        var path=TelemetryStore.Save(chunk,folder);
        using(var doc=JsonDocument.Parse(File.ReadAllText(path)))check(doc.RootElement.GetProperty("records")[0].GetProperty("kind").GetString()=="event","queued telemetry matches receiver schema");
        TelemetryStore.Save(chunk,folder);check(Directory.GetFiles(folder,"*.json").Length==1,"retrying a queued chunk keeps its stable ID");
        var huge=chunk with {Records=new[]{new TelemetryStore.Row("context",DateTimeOffset.UtcNow,new(){["data"]=new string('x',TelemetryStore.MaxChunkBytes)})}};
        var rejected=false;try{TelemetryStore.Save(huge,folder);}catch(InvalidDataException){rejected=true;}
        check(rejected,"oversized queue entries are rejected before writing");
        TelemetryStore.Prune(folder,1);check(!File.Exists(path),"offline queue enforces a hard storage limit");
        var disabled=new TelemetrySettings();check(TelemetryStore.Upload(disabled,CancellationToken.None,folder).GetAwaiter().GetResult()==0,"disabled sharing never uploads queued records");
    }
}
