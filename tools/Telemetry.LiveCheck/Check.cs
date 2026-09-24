using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Clonedivers;

static class TelemetryLiveCheck
{
    static async Task Main(string[] args)
    {
        if(args.Length is <1 or >2)throw new ArgumentException("Supply the local QA output directory and optional deployed endpoint.");
        var endpoint=args.Length==2?args[1]:"https://clonedivers-telemetry.goodecraft.com/";
        if(endpoint is not ("https://clonedivers-telemetry.goodecraft.com/" or "https://clonedivers-telemetry.goode-ventures.workers.dev/"))throw new ArgumentException("Unexpected production endpoint");
        var owner=Environment.GetEnvironmentVariable("CLONEDIVERS_OWNER_TOKEN") ?? throw new Exception("Owner credential missing");
        var invite=Environment.GetEnvironmentVariable("CLONEDIVERS_INVITATION") ?? throw new Exception("Invitation missing");
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(30)};
        using(var denied=await http.GetAsync(endpoint+"admin/sessions"))if(denied.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Unauthenticated read was not rejected");
        var credentials=await TelemetryStore.Enroll(endpoint,"QA - collector self-test",invite);
        var session=Guid.NewGuid().ToString("N");var chunkId=Guid.NewGuid().ToString("N");
        using var current=Process.GetCurrentProcess();using var machine=new TelemetryMachine(current.Id);
        var settings=Settings.Load();
        var context=TelemetryMachine.Context(new(settings.InstalledPackVersion,settings.InstalledGameBuild,settings.InstalledTextureProfile,"Clonedivers",null,null,null));
        context["captureScope"]="Collector self-test, not gameplay";
        var rows=new List<TelemetryStore.Row>{new("context",DateTimeOffset.UtcNow,context),new("resource",DateTimeOffset.UtcNow,machine.Sample(current))};
        await Task.Delay(2100);rows.Add(new("resource",DateTimeOffset.UtcNow,machine.Sample(current)));
        var chunk=new TelemetryStore.Chunk(1,credentials.DeviceId,session,chunkId,credentials.Nickname,rows);
        var file=TelemetryStore.Save(chunk,args[0]);
        if(await TelemetryStore.Upload(credentials,CancellationToken.None,args[0])!=1||File.Exists(file))throw new Exception("Client did not acknowledge upload");
        TelemetryStore.Save(chunk,args[0]);if(await TelemetryStore.Upload(credentials,CancellationToken.None,args[0])!=1)throw new Exception("Retry not acknowledged");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",owner);
        using var response=await http.GetAsync(endpoint+"admin/chunk/"+chunkId);response.EnsureSuccessStatusCode();
        using var saved=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if(saved.RootElement.GetProperty("records").GetArrayLength()!=3||saved.RootElement.GetProperty("chunkId").GetString()!=chunkId)throw new Exception("Stored report does not match");
        using var revoke=await http.PostAsync(endpoint+"admin/revoke/"+credentials.DeviceId,new StringContent("{}"));revoke.EnsureSuccessStatusCode();
        TelemetryStore.Save(chunk with{ChunkId=Guid.NewGuid().ToString("N")},args[0]);
        var blocked=false;try{await TelemetryStore.Upload(credentials,CancellationToken.None,args[0]);}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Unauthorized){blocked=true;}
        if(!blocked)throw new Exception("Revoked credential still uploaded");
        var result=new{endpoint,session,chunkId,records=3,source="real hardware and collector-process counters; not gameplay",unauthorizedReadRejected=true,clientUpload=true,duplicateRetry=true,ownerRead=true,revocation=true};
        File.WriteAllText(Path.Combine(args[0],"live-check.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
