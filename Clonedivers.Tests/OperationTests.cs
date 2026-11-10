using Clonedivers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Clonedivers.Tests;

static class OperationTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception("Operation regression: " + message); Console.WriteLine("  PASS  " + message); }
    static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static PackFile F(string name, string content) => new() { Name = name, Size = Encoding.UTF8.GetByteCount(content), Sha256 = Sha(content) };
    static void Put(string dir, string name, string content) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,name),content,new UTF8Encoding(false)); }
    static string GameDir(string root,string name) { var game=Path.Combine(root,name); Directory.CreateDirectory(Path.Combine(game,"data")); Put(Path.Combine(game,"bin"),"helldivers2.exe",""); return game; }
    static async Task<UpdatePlan> Plan(string game,List<PackFile> wanted,HashCache cache,bool active=true,bool verify=true) => Pack.Plan(game,wanted,
        await Pack.InventoryAsync(game,wanted,cache,verify,null,CancellationToken.None),
        await Pack.VerifiedDownloadsAsync(Path.Combine(game,Pack.DownloadFolder),wanted,CancellationToken.None),active);

    public static async Task Run(string root)
    {
        Console.WriteLine("Player operations: parked repair, exact-target recovery, receipts, retained profile caches and bounded downloads");
        var wanted = new List<PackFile> { F("A.patch_0","base"),F("A.patch_1","commando") };
        var game = GameDir(root,"Parked repair"); var receiptPath=Path.Combine(game,"receipt.json");
        Put(Path.Combine(game,ModFiles.OffFolder),"A.patch_0","base"); Put(Path.Combine(game,ModFiles.OffFolder),"A.patch_1","commando");
        var plan=await Plan(game,wanted,new(),false);
        var result=Pack.Apply(game,plan,Path.Combine(game,"plan.json"),"r8",null,null,"build",new(){["commandos"]=true},"lighter",true,receiptPath);
        Check(!result.Interrupted && ModFiles.GetState(game)==ModState.Off && plan.Downloads.Count==0,"repair keeps an intact parked pack parked without downloading");
        var receipt=InstallReceipt.Load(receiptPath,game)!;
        Check(receipt is not null && receipt.MatchesLayout(game) && receipt.VerifiedUtc is not null,"successful verification writes a matching receipt and verification time");
        var preferences=new Settings {TextureProfile="full"}; preferences.RecordInstall(result,preserveTexturePreference:true);
        Check(preferences.TextureProfile=="full" && preferences.InstalledTextureProfile=="lighter","parked repair records installed profile while preserving the next selected preference");
        var legacyPreferences=new Settings {Options=new(){["skinny"]=true}};
        legacyPreferences.RecordInstall(new ApplyResult {TextureProfile="full",Options=new(){["skinny"]=false}},preserveTexturePreference:true);
        Check(legacyPreferences.TextureProfile=="lighter" && legacyPreferences.InstalledTextureProfile=="full","parked repair also preserves an unmigrated legacy Lighter preference");
        var scoped=new Settings {InstalledGamePath=game,InstalledPackVersion="old",InstalledGameBuild="old-build",LastVerifiedUtc=DateTimeOffset.UtcNow,TextureProfile="lighter"};
        Check(!scoped.ForgetInstallationForDifferentGame(game.ToUpperInvariant()+Path.DirectorySeparatorChar),"same normalized game path retains installed metadata");
        Check(scoped.ForgetInstallationForDifferentGame(Path.Combine(root,"Another installation")) && scoped.InstalledPackVersion is null && scoped.LastVerifiedUtc is null && scoped.TextureProfile=="lighter","different autodetected game clears stale installation metadata but preserves the player preference");
        var verifiedAt=receipt!.VerifiedUtc;
        var unchanged=Pack.Apply(game,await Plan(game,wanted,new(),false),Path.Combine(game,"plan.json"),"r8",null,null,"build",new(){["commandos"]=true},"lighter",false,receiptPath);
        Check(unchanged.VerifiedUtc==verifiedAt,"an unchanged installed target retains its earlier verification time without claiming a new check");
        ModFiles.Toggle(game);
        Check(receipt!.MatchesLayout(game),"receipt identity survives an active/parked move");
        File.Delete(Path.Combine(game,"data","A.patch_1"));
        Check(!receipt.MatchesLayout(game),"missing installed file invalidates the receipt layout check");
        Check(InstallReceipt.Load(receiptPath,GameDir(root,"Different game")) is null,"receipt cannot describe another game installation");

        // Recovery at every mutation boundary for a parked target, including new bytes and renumbering.
        int actions=1;
        for(int boundary=0;boundary<=actions;boundary++)
        {
            game=GameDir(root,"Parked boundary "+boundary); var pp=Path.Combine(game,"plan.json");
            Put(Path.Combine(game,ModFiles.OffFolder),"A.patch_0","commando"); Put(Path.Combine(game,ModFiles.OffFolder),"A.patch_1","base");
            var targets=wanted.Concat(new[]{F("A.patch_2","new")}).ToList(); Put(Path.Combine(game,Pack.DownloadFolder),Sha("new"),"new");
            plan=await Plan(game,targets,new(),false); actions=plan.Actions.Count;
            Game.IsRunningCheck=()=>true;
            try { Pack.Apply(game,plan,pp,"saved-version",null,null,"saved-build",new(){["commandos"]=true},"lighter"); }
            catch(InvalidOperationException) { }
            finally { Game.IsRunningCheck=()=>false; }
            foreach(var action in plan.Actions.Take(boundary))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(action.To)!);
                if(action.Op==PlanOp.Copy) File.Copy(action.From,action.To);
                else if(action.Op==PlanOp.Create) File.Create(action.To).Dispose();
                else File.Move(action.From,action.To);
            }
            result=Pack.Replay(game,pp,null,null,Path.Combine(game,"receipt.json"))!;
            Check(result is {Interrupted:false,TargetActive:false,TextureProfile:"lighter"} && ModFiles.GetState(game)==ModState.Off && !Directory.EnumerateFiles(Path.Combine(game,"data")).Any(),$"parked recovery boundary {boundary}: target stays inactive through completion");
        }

        game=GameDir(root,"Download recovery"); var pendingPath=Path.Combine(game,"plan.json"); Put(Path.Combine(game,"data"),"A.patch_0","base");
        Put(Path.Combine(game,Pack.DownloadFolder),Sha("commando"),"commando"); plan=await Plan(game,wanted,new());
        Game.IsRunningCheck=()=>true;
        try { Pack.Apply(game,plan,pendingPath,"saved-version",null,null,"saved-build",new(){["commandos"]=true},"lighter"); } catch(InvalidOperationException) { }
        finally { Game.IsRunningCheck=()=>false; }
        File.Delete(Path.Combine(game,Pack.DownloadFolder,Sha("commando")));
        Check(Pack.Replay(game,pendingPath,null,null) is null,"missing bytes require a recovery download");
        var target=RecoveryTarget.Read(game,pendingPath)!; var unrelatedNewFeed=new PackManifest {Version="new-online-version"};
        var recoveryPlan=await Plan(game,target.Files,new(),target.TargetActive);
        Check(target.PackVersion=="saved-version" && target.Options["commandos"] && target.TextureProfile=="lighter" && recoveryPlan.Downloads.Count==1,"recovery preserves exact version/options/profile with a changed online feed and missing bytes");
        Put(Path.Combine(game,Pack.DownloadFolder),Sha("commando"),"commando");
        result=Pack.Apply(game,recoveryPlan,pendingPath,target.PackVersion,null,null,target.GameBuild,target.Options,target.TextureProfile,true);
        Check(!result.Interrupted && result.PackVersion!=unrelatedNewFeed.Version && result.Options!["commandos"] && ModFiles.ListPatchFiles(Path.Combine(game,"data")).Length==2 && !File.Exists(pendingPath),"download-required recovery completes the saved target and clears its plan");

        game=GameDir(root,"Retained cache"); var cache=new HashCache(); var big=new string('x',4096);
        Put(Path.Combine(game,"data"),"A.patch_0","base"); Put(Path.Combine(game,"data"),"A.patch_1",big);
        var all=new List<PackFile>{F("A.patch_0","base"),F("A.patch_1",big)};
        await Pack.InventoryAsync(game,all,cache,false,null,CancellationToken.None);
        plan=await Plan(game,all.Take(1).ToList(),cache,true,false); Pack.Apply(game,plan,Path.Combine(game,"plan.json"),"r8",cache,Path.Combine(game,"hashes.json"));
        long bytes=0;
        await Pack.InventoryAsync(game,all,cache,false,new Clonedivers.SyncProgress<(long done,long total)>(p=>bytes=Math.Max(bytes,p.done)),CancellationToken.None);
        Check(cache.Entries.Count==2 && bytes==0,"returning to cached inactive profile does not rehash parked bytes");
        await Pack.InventoryAsync(game,all,cache,true,new Clonedivers.SyncProgress<(long done,long total)>(p=>bytes=Math.Max(bytes,p.done)),CancellationToken.None);
        Check(bytes>=4096,"explicit verification still hashes inactive bytes it needs");
        var parkedPath=Path.Combine(game,Pack.OldFolder,"A.patch_1"); var activeTwin=Path.Combine(game,"data","A.patch_1");
        Put(Path.Combine(game,"data"),"A.patch_1",new string('y',4096)); File.SetLastWriteTimeUtc(activeTwin,File.GetLastWriteTimeUtc(parkedPath));
        var twins=await Pack.InventoryAsync(game,all,cache,false,null,CancellationToken.None);
        Check(twins.Single(f=>f.Path==activeTwin).Sha==Sha(new string('y',4096)) && twins.Single(f=>f.Path==parkedPath).Sha==Sha(big),"same-name/size/timestamp files in active and old profiles cannot borrow each other's cached hashes");
        var dl=Path.Combine(game,Pack.DownloadFolder); Put(dl,Sha(big)+Pack.PartialSuffix,"partial"); Put(dl,Sha("obsolete"),"obsolete");
        Pack.CleanDownloadFolder(dl,all.Select(f=>f.Sha256));
        Check(File.Exists(Path.Combine(dl,Sha(big)+Pack.PartialSuffix)) && !File.Exists(Path.Combine(dl,Sha("obsolete"))),"cleanup retaining all supported profiles preserves other-profile partial downloads");

        await DownloadTests(root);
    }

    internal static async Task DownloadTests(string root)
    {
        var oldTimeout=Pack.DownloadInactivityTimeout; var oldDelay=Pack.DownloadRetryDelay;
        Pack.DownloadInactivityTimeout=TimeSpan.FromMilliseconds(150); Pack.DownloadRetryDelay=TimeSpan.FromMilliseconds(10);
        try
        {
            var body=new string('a',2048);
            using(var server=new DownloadServer(body,stallEvery:false))
            {
                var file=F("A.patch_0",body); file.Url=server.Url; var path=Path.Combine(root,"resumed-stall");
                await Pack.DownloadAsync(file,path,null,CancellationToken.None);
                Check(File.ReadAllText(path)==body && server.Requests==2 && server.RangeRequests==1,"stalled body retries once with Range and produces verified complete bytes");
            }
            using(var server=new DownloadServer(body,stallEvery:true))
            {
                var file=F("A.patch_0",body); file.Url=server.Url; var path=Path.Combine(root,"persistent-stall");
                IOException? error=null; var timer=System.Diagnostics.Stopwatch.StartNew();
                try { await Pack.DownloadAsync(file,path,null,CancellationToken.None); } catch(IOException ex) { error=ex; }
                Check(error is not null && server.Requests==9 && timer.Elapsed<TimeSpan.FromSeconds(8) && new FileInfo(path).Length>0,"persistent stalls stop after nine bounded attempts while preserving partial bytes");
            }
            using(var server=new DownloadServer(body,stallEvery:true))
            {
                var file=F("A.patch_0",body); file.Url=server.Url;
                using var cancelled=new CancellationTokenSource(TimeSpan.FromMilliseconds(50)); bool stopped=false;
                try { await Pack.DownloadAsync(file,Path.Combine(root,"cancelled-stall"),null,cancelled.Token); } catch(OperationCanceledException) { stopped=true; }
                Check(stopped && server.Requests==1,"cancelling a stalled download never retries");
            }
            using(var server=new DownloadServer(body,false,statuses:[408,429,500,503,200],stallFirst:false))
            {
                var file=F("launcher.exe",body); file.Url=server.Url;
                var path=Path.Combine(root,"transient-statuses");
                await Pack.DownloadAsync(file,path,null,CancellationToken.None);
                Check(server.Requests==5 && File.ReadAllText(path)==body,"408, 429 and server errors retry through a verified successful download");
            }
            using(var server=new DownloadServer(body,false,cacheFailure:true,stallFirst:false))
            {
                var file=F("launcher.exe",body); file.Url=server.Url+"?existing=yes";
                var path=Path.Combine(root,"cache-fallback");
                await Pack.DownloadAsync(file,path,null,CancellationToken.None);
                Check(server.Requests==3 && server.Targets.Take(2).All(t=>t=="/asset?existing=yes") &&
                    server.Targets.Last().StartsWith("/asset?existing=yes&clonedivers_retry=") && File.ReadAllText(path)==body,
                    "repeated pinned-path 504s use a cache-busting query while retaining path, existing query and byte verification");
            }
            using(var server=new DownloadServer(body,false,statuses:[404],stallFirst:false))
            {
                var file=F("launcher.exe",body); file.Url=server.Url; bool missing=false;
                try { await Pack.DownloadAsync(file,Path.Combine(root,"missing-asset"),null,CancellationToken.None); }
                catch(HttpRequestException error) { missing=error.StatusCode==HttpStatusCode.NotFound; }
                Check(missing && server.Requests==1,"a permanent HTTP failure returns immediately without retries");
            }
            using(var server=new DownloadServer(body,false,cacheFailure:true,stallFirst:false))
            {
                var file=F("launcher.exe",body); file.Url=server.Url; file.Sha256=Sha("different bytes"); bool rejected=false;
                try { await Pack.DownloadAsync(file,Path.Combine(root,"bad-cache-bytes"),null,CancellationToken.None); }
                catch(InvalidDataException) { rejected=true; }
                Check(rejected && server.Requests==3 && !File.Exists(Path.Combine(root,"bad-cache-bytes")),"cache-busting never bypasses SHA-256 verification");
            }
            using(var server=new DownloadServer(body,false,statuses:[504],stallFirst:false))
            {
                var file=F("unpinned",body); file.Url=server.Url; file.Sha256=""; bool failed=false;
                try { await Pack.DownloadAsync(file,Path.Combine(root,"unpinned-asset"),null,CancellationToken.None); }
                catch(HttpRequestException) { failed=true; }
                Check(failed && server.Requests==9 && server.Targets.All(t=>!t.Contains('?')),"retry exhaustion keeps unverified URLs unchanged");
            }
            using(var server=new DownloadServer(body,false,stallFirst:false))
            {
                var file=F("oversized",body); file.Url=server.Url; file.Size=128; bool rejected=false;
                var path=Path.Combine(root,"oversized-asset");
                try { await Pack.DownloadAsync(file,path,null,CancellationToken.None); }
                catch(InvalidDataException) { rejected=true; }
                Check(rejected && server.Requests==1 && !File.Exists(path),"the shared downloader enforces the appearance export's size bound while streaming");
            }
            using(var server=new DownloadServer(body,false,statuses:[504],stallFirst:false))
            {
                Pack.DownloadRetryDelay=TimeSpan.FromSeconds(30);
                var file=F("launcher.exe",body); file.Url=server.Url;
                using var cancelled=new CancellationTokenSource(100); bool stopped=false;
                try { await Pack.DownloadAsync(file,Path.Combine(root,"cancelled-backoff"),null,cancelled.Token); }
                catch(OperationCanceledException) { stopped=true; }
                Check(stopped && server.Requests==1,"cancellation interrupts retry backoff immediately");
            }
        }
        finally { Pack.DownloadInactivityTimeout=oldTimeout; Pack.DownloadRetryDelay=oldDelay; }
    }

    sealed class DownloadServer : IDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0); readonly CancellationTokenSource stopping=new();
        readonly byte[] body; readonly bool stallEvery,stallFirst,cacheFailure; readonly int[]? statuses; int requests,rangeRequests;
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Targets=new();
        public int Requests=>requests; public int RangeRequests=>rangeRequests;
        public string Url {get;}
        public DownloadServer(string content,bool stallEvery,int[]? statuses=null,bool cacheFailure=false,bool stallFirst=true)
        {
            body=Encoding.UTF8.GetBytes(content); this.stallEvery=stallEvery; this.statuses=statuses; this.cacheFailure=cacheFailure; this.stallFirst=stallFirst; listener.Start();
            Url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/asset"; _=Accept();
        }
        async Task Accept()
        {
            try { while(!stopping.IsCancellationRequested) { var client=await listener.AcceptTcpClientAsync(stopping.Token); _=Respond(client); } }
            catch(OperationCanceledException) { } catch(SocketException) { }
        }
        async Task Respond(TcpClient client)
        {
            using(client)
            try
            {
                using var stream=client.GetStream(); using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var request=Interlocked.Increment(ref requests); int offset=0; string? line;
                var target=(await reader.ReadLineAsync(stopping.Token))!.Split(' ')[1]; Targets.Enqueue(target);
                while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(stopping.Token)))
                    if(line.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)) { offset=int.Parse(line[13..].Split('-')[0]); Interlocked.Increment(ref rangeRequests); }
                var status=cacheFailure && !target.Contains("clonedivers_retry=") ? 504 : statuses?[Math.Min(request-1,statuses.Length-1)] ?? 200;
                if(status!=200)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),stopping.Token);
                    return;
                }
                var headers=$"HTTP/1.1 {(offset>0?"206 Partial Content":"200 OK")}\r\nContent-Length: {body.Length-offset}\r\nConnection: close\r\n";
                if(offset>0) headers+=$"Content-Range: bytes {offset}-{body.Length-1}/{body.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers+"\r\n"),stopping.Token);
                bool stall=stallEvery||stallFirst&&request==1; var take=stall?Math.Min(128,body.Length-offset):body.Length-offset;
                await stream.WriteAsync(body.AsMemory(offset,take),stopping.Token); await stream.FlushAsync(stopping.Token);
                if(stall) await Task.Delay(TimeSpan.FromSeconds(3),stopping.Token);
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        public void Dispose() { stopping.Cancel(); listener.Stop(); }
    }
}
