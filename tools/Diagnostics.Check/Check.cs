using Clonedivers;
static class DiagnosticsCheck
{
    static int checks;
    static void Check(bool ok,string description) { if(!ok) throw new Exception(description); checks++; Console.WriteLine("PASS "+description); }
    static void Main()
    {
        var gpu = new Diagnostics.Adapter("Fixture GPU",32UL*1024*1024*1024,false);
        var hw = new Diagnostics.Hardware("Fixture CPU",16,64UL*1024*1024*1024,"Windows fixture",new[]{"1920x1080 primary"},new[]{gpu});
        var text=Diagnostics.FormatHardware(hw);
        Check(text.Contains("32.00 GiB"),"GPU capacity retains values above 4 GiB");
        Check(text.Contains("64.00 GiB"),"Physical RAM uses wide capacity");
        Check(text.Contains("not current usage"),"Capacity is not presented as runtime measurement");
        Check(Diagnostics.FormatHardware(hw with { Adapters=Array.Empty<Diagnostics.Adapter>(),PhysicalBytes=null,Displays=Array.Empty<string>()}).Contains("unavailable"),"Missing hardware explicit");
        Check(Diagnostics.ErrorCategory(@"Access denied C:\Users\SecretName\file.txt") == "file access","Exception details withheld");
        Check(Diagnostics.ErrorCategory("https://host/account?id=secret") == "reported (message omitted for privacy)","Unknown errors do not disclose URLs");
        var settings=new Settings { GamePath=@"C:\Users\SecretName\game",ManifestUrl="https://secret.invalid/token",InstalledPackVersion=@"C:\private",InstalledGameBuild="24826606" };
        var report=Diagnostics.Build(settings,null,null,@"Unknown SecretName");
        Check(!report.Contains("SecretName") && !report.Contains("secret.invalid") && !report.Contains(@"C:\private"),"Report excludes supplied personal paths and URLs");
        Check(report.Contains("24826606") && report.Contains("Integrity now: not checked"),"Recorded metadata distinguished from fresh integrity");
        var historyPath=Path.Combine(Path.GetTempPath(),"clonedivers-history-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            SupportHistory.Record("error",@"C:\Users\SecretName",@"C:\Users\SecretName",@"access C:\Users\SecretName",historyPath);
            var history=Diagnostics.FormatHistory(SupportHistory.Read(historyPath));
            Check(!history.Contains("SecretName") && history.Contains("file access"),"Recorded history keeps only safe error category");
            File.WriteAllText(historyPath,"[{\"Utc\":\"2026-09-13T00:00:00Z\",\"Event\":\"error\",\"Pack\":\"C:\\\\Users\\\\SecretName\",\"Profile\":\"https://secret.invalid\",\"ErrorCategory\":\"C:\\\\Users\\\\SecretName\"}]");
            history=Diagnostics.FormatHistory(SupportHistory.Read(historyPath));
            Check(!history.Contains("SecretName") && !history.Contains("secret.invalid"),"Tampered on-disk history is sanitized on read and formatting");
            var forged=Diagnostics.FormatHistory(new[]{new SupportHistory.Entry(DateTimeOffset.UtcNow,@"C:\SecretName","SecretName","SecretName","SecretName")});
            Check(!forged.Contains("SecretName"),"Unknown history event omitted");
        }
        finally { File.Delete(historyPath); File.Delete(historyPath+".tmp"); }
        var live=Diagnostics.ReadHardware();
        Console.WriteLine(Diagnostics.FormatHardware(live));
        Check(live.LogicalProcessors>0,"Native hardware read returns processor count");
        Console.WriteLine($"{checks} diagnostics checks passed.");
    }
}
