// Observation-only controller for the unmodified native codecs and header probe.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using netDxf;
internal static partial class Program
{
    private static string CodecText(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? new string(value.GetProperty("utf16").EnumerateArray().Select(x => (char)x.GetInt32()).ToArray()) : value.GetString()!;
    private static object? CodecValue(object? value) => value switch {
        null => null, string s => new { utf16=s.Select(c=>(int)c).ToArray() }, byte[] bytes => new { bytes=bytes.Select(x=>(int)x).ToArray() },
        bool flag => flag, long wide => new { int64=wide.ToString(CultureInfo.InvariantCulture) },
        double real => new { number=Bits(real) }, _ when value is byte or short or int => new { number=Bits(Convert.ToDouble(value,CultureInfo.InvariantCulture)) },
        _ => throw new InvalidOperationException("Unobserved codec value type: "+value.GetType().FullName)
    };
    private static object CodecError(Exception error, bool message) {
        while(error is TargetInvocationException && error.InnerException != null) error=error.InnerException;
        return new {name=error.GetType().Name,param=(error as ArgumentException)?.ParamName,message=message?error.Message:null};
    }
    private sealed class CodecInputStream : Stream {
        public readonly List<object> Calls=new(); public readonly byte[] Bytes;
        private readonly JsonElement options; public long Offset; public int Attempts; public bool Closed;
        public CodecInputStream(JsonElement input) { options=input; Bytes=input.GetProperty("bytes").EnumerateArray().Select(x=>x.GetByte()).ToArray(); Offset=Get("origin",0); }
        private int Get(string key,int fallback)=>options.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        private bool Flag(string key,bool fallback)=>options.TryGetProperty(key,out var value)?value.GetBoolean():fallback;
        public override bool CanRead=>!Closed&&Flag("readable",true);
        public override bool CanSeek=>Flag("seekable",true);
        public override bool CanWrite=>false;
        public override long Length { get {if(!CanSeek)throw new NotSupportedException("Length is unavailable.");return Bytes.LongLength;} }
        public override long Position { get {if(!CanSeek)throw new NotSupportedException("Position is unavailable.");return Offset;} set {Calls.Add(new {method="Position",value});if(Get("failPositionSet",0)!=0)throw new IOException("Injected position failure.");Offset=value;} }
        private void Before(string method,int count) { Attempts++; Calls.Add(new {method,count,offset=Offset});if(Attempts==Get("failAt",-1))throw new IOException("Injected stream read failure.");if(Closed)throw new ObjectDisposedException("codec-input"); }
        public override int Read(byte[] buffer,int offset,int count) { Before("Read",count);int n=Math.Min(Math.Min(count,Get("fragment",int.MaxValue)),Math.Max(0,Bytes.Length-(int)Offset));Array.Copy(Bytes,Offset,buffer,offset,n);Offset+=n;return n; }
        public override int ReadByte() {Before("ReadByte",1);return Offset<Bytes.LongLength?Bytes[Offset++]:-1;}
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void Flush(){} public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    private sealed class CodecInputText : TextReader {
        private readonly StringReader reader; private readonly int failAt; public int Attempts; public bool Closed; public readonly List<object> Calls=new();
        public CodecInputText(JsonElement input){reader=new StringReader(CodecText(input.GetProperty("text")));failAt=input.TryGetProperty("failAt",out var f)?f.GetInt32():-1;}
        public override string? ReadLine(){Attempts++;Calls.Add(new {method="ReadLine",attempt=Attempts});if(Attempts==failAt)throw new IOException("Injected text read failure.");return reader.ReadLine();}
        protected override void Dispose(bool disposing){Closed=true;reader.Dispose();base.Dispose(disposing);}
    }
    private static object CodecReadersRequest(JsonElement input) {
        string mode=input.GetProperty("mode").GetString()!;
        var results=new List<object>();object? instance=null,constructionError=null;
        CodecInputStream? stream=null;CodecInputText? text=null;
        Type type=typeof(DxfDocument).Assembly.GetType("netDxf.IO."+(mode=="text"?"TextCodeValueReader":"BinaryCodeValueReader"))!;
        if(mode=="text")text=new CodecInputText(input);else stream=new CodecInputStream(input);
        bool nullReader=input.TryGetProperty("nullReader",out var nr)&&nr.GetBoolean();
        if(mode!="probe")try {
            if(mode=="text")instance=Activator.CreateInstance(type,new object?[]{nullReader?null:text});
            else {
                string encoding=input.TryGetProperty("encoding",out var en)?en.GetString()!:"strict";
                Encoding selected=encoding=="ascii"?Encoding.ASCII:encoding=="latin1"?Encoding.Latin1:new UTF8Encoding(false,encoding=="strict");
                var binary=nullReader?null:new BinaryReader(stream!,selected,true);
                instance=Activator.CreateInstance(type,new object?[]{binary,input.TryGetProperty("nullEncoding",out var ne)&&ne.GetBoolean()?null:selected,input.TryGetProperty("legacy",out var lg)&&lg.GetBoolean()});
            }
        } catch(Exception error){constructionError=CodecError(error,true);}
        object? Position(){try{return type.GetProperty("CurrentPosition")!.GetValue(instance);}catch(Exception e){return new {error=CodecError(e,false)};}}
        object? State()=>instance==null?null:new {code=(short)type.GetProperty("Code")!.GetValue(instance)!,value=CodecValue(type.GetProperty("Value")!.GetValue(instance)),position=Position()};
        object Host()=>mode=="text"?new {calls=text!.Calls.ToArray(),closed=text.Closed}:new {calls=stream!.Calls.ToArray(),closed=stream.Closed,offset=stream.Offset};
        object? initial=State();
        if(constructionError==null)foreach(var step in input.GetProperty("steps").EnumerateArray()){
            string method=step.GetProperty("method").GetString()!;object? result=null,error=null;
            try {
                if(mode=="probe") {
                    bool binary=false;
                    if(method=="Public") {var found=DxfDocument.CheckDxfFileVersion(nullReader?null:stream,out binary);result=new {version=(int)found,binary};}
                    else {
                        var args=new object?[]{nullReader?null:stream,step.TryGetProperty("variable",out var variable)&&variable.ValueKind!=JsonValueKind.Null?CodecText(variable):null,binary};
                        var value=typeof(DxfDocument).Assembly.GetType("netDxf.IO.DxfReader")!.GetMethod("CheckHeaderVariable",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args);
                        result=new {text=CodecValue(value),binary=(bool)args[2]!};
                    }
                }else if(method=="Code5IsString"||method=="SkipComments")type.GetProperty(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(instance,step.GetProperty("value").GetBoolean());
                else if(method=="Snapshot")result=CodecValue(type.GetProperty("Value")!.GetValue(instance));
                else result=CodecValue(type.GetMethod(method,Type.EmptyTypes)!.Invoke(instance,null));
            }catch(Exception e){error=CodecError(e,method=="Next"||mode=="probe");}
            results.Add(new {value=result,error,state=State(),host=Host()});
        }
        return new {constructor=constructionError,initial,results,host=Host()};
    }
}
