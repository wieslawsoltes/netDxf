// Filesystem oracle invokes the unmodified pinned production SaveAtomic/helper on real files.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using netDxf;
using netDxf.IO;
internal static class FileSystemOracle
{
    private static readonly byte[] Original = Enumerable.Range(0,257).Select(i=>(byte)i).ToArray();
    private static object Attempt(Action action) {
        try { action(); return new { ok=true, error=(string?)null, param=(string?)null }; }
        catch(Exception e) { return new { ok=false, error=e.GetType().Name, param=(e as ArgumentException)?.ParamName }; }
    }
    private static void Write(string name, Action<FileStream> action, CancellationToken token) {
        var method=typeof(DxfDocument).Assembly.GetType("netDxf.IO.DxfAtomicFile",true)!.GetMethod("Write",BindingFlags.Static|BindingFlags.NonPublic)!;
        try { method.Invoke(null,new object[]{name,action,token}); }
        catch(TargetInvocationException e) when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
    }
    internal static object Execute(JsonElement request) {
        var directory=Path.Combine(Path.GetTempPath(),"netdxf-fs-oracle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var name=Path.Combine(directory,"Zażółć 東京 drawing.dxf");
        bool existing=request.GetProperty("existing").GetBoolean();
        if(existing)File.WriteAllBytes(name,Original);
        using var cancel=new CancellationTokenSource();
        if(request.TryGetProperty("cancel",out var c)&&c.GetBoolean())cancel.Cancel();
        string? before=null; bool? original=null;
        FileStream? reader=existing?new FileStream(name,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete):null;
        try {
            var mode=request.GetProperty("mode").GetString();
            var operation=Attempt(()=>{
                if(mode=="raw") {
                    using var input=new MemoryStream(request.GetProperty("bytes").GetBytesFromBase64(),false);
                    var raw=DxfRawDocument.Load(input);
                    if(request.TryGetProperty("maximumBytes",out var budget))raw=DxfRawDocument.Create(raw.Tags,raw.IsBinary,new DxfRawOptions(budget.GetInt32()));
                    if(request.TryGetProperty("binary",out var b))raw.SaveAtomic(name,b.GetBoolean(),cancel.Token);
                    else raw.SaveAtomic(name,cancel.Token);
                    original=raw.HasOriginalBytes;
                } else if(mode=="staged") {
                    int failure=request.GetProperty("failure").GetInt32();
                    Write(name,stream=>{
                        stream.Write(new byte[123456]);
                        before=File.Exists(name)?Convert.ToBase64String(File.ReadAllBytes(name)):null;
                        if(failure==0)throw new IOException("Injected write failure.");
                        if(failure==1)cancel.Cancel();
                        else if(failure==2)stream.Dispose();
                        else if(failure==3) {if(existing)File.Delete(name);else File.WriteAllBytes(name,Original);}
                    },cancel.Token);
                } else if(mode=="path") {
                    string? target=name;
                    switch(request.GetProperty("pathCase").GetString()) {
                        case "null":target=null;break;
                        case "empty":target="";break;
                        case "nul":target=name+"\0";break;
                        case "directory":target=directory;break;
                        case "missing-parent":target=Path.Combine(directory,"missing","drawing.dxf");break;
                        case "readonly":File.WriteAllBytes(name,Original);File.SetAttributes(name,File.GetAttributes(name)|FileAttributes.ReadOnly);break;
                        case "symlink":target=name+".link";File.CreateSymbolicLink(target,name);break;
                        case "dangling":target=name+".link";File.CreateSymbolicLink(target,name+".absent");break;
                    }
                    Write(target!,s=>s.Write(new byte[]{1,2,3}),cancel.Token);
                } else throw new ArgumentException("Unknown filesystem operation.");
            });
            string? held=null;
            if(reader!=null) {using var copy=new MemoryStream();reader.CopyTo(copy);held=Convert.ToBase64String(copy.ToArray());}
            return new { operation, exists=File.Exists(name), bytes=File.Exists(name)?Convert.ToBase64String(File.ReadAllBytes(name)):null,
                heldReader=held, beforePublish=before, original, temporaryCount=Directory.EnumerateFiles(directory,".netdxf-*.tmp").Count() };
        } finally {
            reader?.Dispose();
            if(File.Exists(name))File.SetAttributes(name,FileAttributes.Normal);
            Directory.Delete(directory,true);
        }
    }
}
