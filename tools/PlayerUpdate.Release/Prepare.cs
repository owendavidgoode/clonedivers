using System.Runtime.InteropServices;
using System.Text.Json;
using Clonedivers;

// Materialize read-only release-check inputs without duplicating the complete pack.
// All output is new; the ordinary publisher seals its own copies of new assets.
static class PlayerUpdateRelease
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateHardLink(string target, string source, IntPtr reserved);

    static async Task Main(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("candidate-manifest output-directory asset-directory source-directory [source-directory ...]");
        var manifestPath = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Choose a fresh profile directory.");
        var pack = Manifest.Parse(File.ReadAllText(manifestPath)).Pack!;
        var wanted = pack.Files.Where(f => f.Size > 0).GroupBy(f => f.Sha256).ToDictionary(g => g.Key,g => g.First());
        var sizes = wanted.Values.Select(f => f.Size).ToHashSet();
        var sources = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in args.Skip(2))
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var info = new FileInfo(path);
                if (!sizes.Contains(info.Length)) continue;
                var name = Path.GetFileName(path);
                if (!ModFiles.TryParsePatchName(name,out _,out _,out _) && !wanted.ContainsKey(name)) continue;
                if (wanted.ContainsKey(name) && sources.ContainsKey(name)) continue;
                var hash = await Pack.Sha256Async(path,CancellationToken.None);
                if (wanted.TryGetValue(hash,out var file) && file.Size==info.Length) sources.TryAdd(hash,Path.GetFullPath(path));
            }
            Console.WriteLine($"Indexed {folder}: {sources.Count}/{wanted.Count} required unique assets available.");
        }
        var specs = new List<object>();
        var totals = new List<object>();
        foreach (var profile in PackProfiles.Supported(pack))
        {
            bool Enabled(string id) => id=="skinny" ? profile.Id!="full" : pack.Options.FirstOrDefault(o=>o.Id==id)?.Default ?? true;
            var files = Pack.EffectiveFiles(pack,Enabled,profile.Id);
            FileSafety.ValidateTargets(files);
            var missing=files.Where(f=>f.Size>0&&!sources.ContainsKey(f.Sha256)).Select(f=>f.Name).ToList();
            if(missing.Count>0) throw new IOException("Missing verified source bytes: "+string.Join(", ",missing));
            var folder=Path.Combine(output,profile.Id);
            Directory.CreateDirectory(folder);
            foreach(var file in files)
            {
                var target=FileSafety.Child(folder,file.Name);
                if(file.Size==0) File.WriteAllBytes(target,Array.Empty<byte>());
                else if(!CreateHardLink(target,sources[file.Sha256],IntPtr.Zero))
                    throw new IOException($"Hard link failed ({Marshal.GetLastWin32Error()}): {target}");
            }
            specs.Add(new {id=profile.Id,directory=folder});
            totals.Add(new {profile=profile.Id,files=files.Count,bytes=files.Sum(f=>f.Size)});
        }
        var json=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(output,"directories.json"),JsonSerializer.Serialize(specs,json));
        File.WriteAllText(Path.Combine(output,"inventory.json"),JsonSerializer.Serialize(new{manifestPath,manifestSha256=await Pack.Sha256Async(manifestPath,CancellationToken.None),totals},json));
        Console.WriteLine("Prepared verified profile inputs; do not edit these hard-linked files.");
    }
}
