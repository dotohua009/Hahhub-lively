using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyProduct("hahhub")]

internal sealed class Updates
{
    const string Channel="https://raw.githubusercontent.com/dotohua009/keybloom-lively/main/downloads/hahhub/";
    const string Package="hahhub-windows-x64.zip";
    const int MaxPackage=32*1024*1024;
    static readonly HttpClient client=new HttpClient{Timeout=TimeSpan.FromSeconds(60),MaxResponseContentBufferSize=MaxPackage};
    public static readonly Version Current=new Version(typeof(Updates).Assembly.GetName().Version.ToString(3));
    public sealed class Manifest {
        public string product{get;set;}public string version{get;set;}
        public string sha256{get;set;}public long bytes{get;set;}
    }
    public readonly Version Version;
    readonly Manifest manifest;
    Updates(Manifest info,Version version){manifest=info;Version=version;}

    internal static Updates Parse(string json){
        var m=Project.Json.Deserialize<Manifest>(json);
        Version v;
        if(m==null||m.product!="hahhub"||!System.Version.TryParse(m.version,out v)||v.Build<0||v.ToString(3)!=m.version||
           m.bytes<1||m.bytes>MaxPackage||m.sha256==null||!Regex.IsMatch(m.sha256,@"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("Invalid update manifest");
        return new Updates(m,v);
    }
    public static async Task<Updates> Check(){
        using(var response=await client.GetAsync(Channel+"update.json").ConfigureAwait(false)){
            if(response.StatusCode==HttpStatusCode.NotFound)return null;
            response.EnsureSuccessStatusCode();
            string json=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if(json.Length>4096)throw new InvalidDataException("Invalid update manifest");
            return Parse(json);
        }
    }
    static readonly string[] Required={"HAHHAN Studio.exe","Studio.xaml","studio.ico","cover.jpg","donation.png",
        "Microsoft.Web.WebView2.Core.dll","WebView2Loader.dll","apply-update.ps1",
        "effects/hahhan-glass/glass-data.js","effects/hahhan-blackhole/thumbnail.jpg"};
    internal void Unpack(byte[] data,string folder){
        using(var sha=SHA256.Create())
            if(data.LongLength!=manifest.bytes||!String.Equals(BitConverter.ToString(sha.ComputeHash(data)).Replace("-",""),manifest.sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum mismatch");
        using(var zip=new ZipArchive(new MemoryStream(data),ZipArchiveMode.Read)){
            long bytes=0;
            if(zip.Entries.Count>512)throw new InvalidDataException("Too many entries");
            foreach(var entry in zip.Entries){
                // Release packages contain files only. Forbid traversal, NTFS streams and device names.
                var parts=entry.FullName.Split('/');
                if(parts.Any(p=>!Regex.IsMatch(p,@"\A[A-Za-z0-9_-][A-Za-z0-9_. -]*\z")||p.EndsWith(".")||p.EndsWith(" ")||
                    Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)",RegexOptions.IgnoreCase))||
                    (parts.Length>1&&parts[0]!="effects")||(bytes+=entry.Length)>128L*1024*1024)
                    throw new InvalidDataException("Invalid update file");
                string path=Path.Combine(folder,Path.Combine(parts));Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var input=entry.Open())using(var file=new FileStream(path,FileMode.CreateNew))input.CopyTo(file);
            }
        }
        if(Required.Any(p=>!File.Exists(Path.Combine(folder,p))))throw new InvalidDataException("Incomplete update");
        var actual=AssemblyName.GetAssemblyName(Path.Combine(folder,Required[0]));
        if(actual.Name!="HAHHAN Studio"||actual.Version.ToString(3)!=Version.ToString())throw new InvalidDataException("Unexpected version");
    }
    public async Task Start(){
        if(Version<=Current)throw new InvalidOperationException("Update is not newer");
        byte[] data=await client.GetByteArrayAsync(Channel+"v"+Version+"/"+Package).ConfigureAwait(false);
        var app=Project.Base.TrimEnd(Path.DirectorySeparatorChar);
        var job=Path.Combine(app,".hahhub-update-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try{
            Unpack(data,Path.Combine(job,"next"));
            // Run the installed helper, not a script from the downloaded archive.
            var helper=Path.Combine(job,"apply.ps1");File.Copy(Path.Combine(app,"apply-update.ps1"),helper);
            StartHelper(helper,"-ParentId "+Process.GetCurrentProcess().Id);
        }catch{Directory.Delete(job,true);throw;}
    }
    static void StartHelper(string helper,string option){
        var shell=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe");
        var args="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+helper+"\" "+option;
        using(var process=Process.Start(new ProcessStartInfo(shell,args){WorkingDirectory=Path.GetTempPath(),UseShellExecute=false,CreateNoWindow=true}))
            if(process==null)throw new IOException("Could not start updater");
    }
    public static bool RecoverIfNeeded(){
        Mutex active;if(Mutex.TryOpenExisting(@"Local\hahhub.Update.Apply",out active)){active.Dispose();return true;}
        foreach(var job in Directory.EnumerateDirectories(Project.Base,".hahhub-update-*")){
            if(!Regex.IsMatch(Path.GetFileName(job),@"^\.hahhub-update-[a-f0-9]{32}$")||
               (File.GetAttributes(job)&FileAttributes.ReparsePoint)!=0||!File.Exists(Path.Combine(job,"applying.json")))continue;
            StartHelper(Path.Combine(job,"apply.ps1"),"-Recover");return true;
        }
        return false;
    }
}
