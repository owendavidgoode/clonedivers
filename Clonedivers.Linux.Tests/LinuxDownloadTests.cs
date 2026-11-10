using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using Clonedivers;

internal static class LinuxDownloadTests
{
    static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static PackFile F(string name, string body) => new() { Name = name, Size = Encoding.UTF8.GetByteCount(body), Sha256 = Sha(body) };
    internal static async Task Run(string root, Action<bool, string> check)
    {
        void Check(bool ok, string name) => check(ok, name);
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
