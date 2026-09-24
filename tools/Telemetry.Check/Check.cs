using System.Diagnostics;
using Clonedivers;

static class TelemetryCheck
{
    static async Task Main()
    {
        using var process=Process.GetCurrentProcess();using var sampler=new TelemetryMachine(process.Id);
        var clock=Stopwatch.StartNew();var first=sampler.Sample(process);var firstMs=clock.Elapsed.TotalMilliseconds;
        await Task.Delay(2100);clock.Restart();var next=sampler.Sample(process);var nextMs=clock.Elapsed.TotalMilliseconds;
        if(next.GetValueOrDefault("privateBytes") is not long bytes || bytes<=0 || next.GetValueOrDefault("gameCpuPercentAllCores") is not double)throw new Exception("Resource sampling failed");
        var context=TelemetryMachine.Context(new(null,null,null,null,null,null,null));
        var crashes=await TelemetryMachine.CrashEvents(DateTimeOffset.UtcNow.AddHours(-4),CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{firstSampleMs=firstMs,nextSampleMs=nextMs,resourceFields=next.Count,availableResourceFields=next.Count(f=>f.Value is not null),contextFields=context.Count,recentHd2CrashEvents=crashes.Count}));
    }
}
