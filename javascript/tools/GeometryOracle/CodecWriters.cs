// Observation-only host: the pinned native writers supply every serialized value.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using netDxf;
internal static partial class Program {
    private sealed class CodecOutputStream : Stream {
        private readonly JsonElement input; private readonly MemoryStream data=new();
        public readonly List<object> Calls=new();public bool Closed;public int Attempts,Flushes;public Action? Hook;
        public CodecOutputStream(JsonElement input){this.input=input;}
        private int Option(string name,int fallback)=>input.TryGetProperty(name,out var value)?value.GetInt32():fallback;
        public override bool CanRead=>false;public override bool CanSeek=>!input.TryGetProperty("seekable",out var seek)||seek.GetBoolean();
        public override bool CanWrite=>!Closed;
        public override long Length=>data.Length;
        public override long Position{get{if(!CanSeek)throw new NotSupportedException("Position is unavailable.");return data.Position;}set=>throw new NotSupportedException();}
        public byte[] Bytes=>data.ToArray();
        private void Before(string method,int count){Attempts++;Calls.Add(new{method,count});if(Attempts==Option("hookAt",-1))Hook?.Invoke();}
        private void Output(byte[] buffer,int offset,int count){
            if(Attempts==Option("failAt",-1)){int take=Math.Min(count,Option("partial",0));data.Write(buffer,offset,take);throw new IOException("Injected output failure.");}
            data.Write(buffer,offset,count);
        }
        public override void Write(byte[] buffer,int offset,int count){Before("Write",count);Output(buffer,offset,count);}
        public override void WriteByte(byte value){Before("WriteByte",1);Output(new[]{value},0,1);}
        public override void Flush(){Calls.Add(new{method="Flush",count=0});if(++Flushes==Option("failFlush",-1))throw new IOException("Injected flush failure.");}
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
    private sealed class CodecOutputText : TextWriter {
        private readonly JsonElement input;public readonly StringBuilder Text=new();public readonly List<object> Calls=new();public int Attempts,Flushes;public bool Closed;public Action? Hook;
        public CodecOutputText(JsonElement input){this.input=input;NewLine=input.TryGetProperty("newline",out var nl)?CodecText(nl):"\n";}
        public override Encoding Encoding=>Encoding.UTF8;
        private void Emit(string kind,string? value){
            Attempts++;Calls.Add(new{method=kind,value=CodecValue(value)});
            if(input.TryGetProperty("hookAt",out var at)&&at.GetInt32()==Attempts)Hook?.Invoke();
            if(input.TryGetProperty("failAt",out var fail)&&fail.GetInt32()==Attempts)throw new IOException("Injected output failure.");
            Text.Append(value);Text.Append(NewLine);
        }
        public override void WriteLine(string? value)=>Emit("WriteLine.String",value);
        public override void WriteLine(int value)=>Emit("WriteLine.Int32",value.ToString(FormatProvider));
        public override void Flush(){Calls.Add(new{method="Flush",value=(object?)null});if(input.TryGetProperty("failFlush",out var fail)&&fail.GetInt32()==++Flushes)throw new IOException("Injected flush failure.");}
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
    }
    private static object? CodecWriterInput(JsonElement value){
        if(value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind==JsonValueKind.String)return value.GetString();
        if(value.ValueKind==JsonValueKind.True||value.ValueKind==JsonValueKind.False)return value.GetBoolean();
        if(value.TryGetProperty("utf16",out _))return CodecText(value);
        if(value.TryGetProperty("int16",out var shortValue))return shortValue.GetInt16();
        if(value.TryGetProperty("int32",out var intValue))return intValue.GetInt32();
        if(value.TryGetProperty("byte",out var byteValue))return byteValue.GetByte();
        if(value.TryGetProperty("int64",out var longValue))return long.Parse(longValue.GetString()!,CultureInfo.InvariantCulture);
        if(value.TryGetProperty("double",out var realValue))return FromBits(realValue.GetString()!);
        if(value.TryGetProperty("repeat",out var repeat))return string.Concat(Enumerable.Repeat(CodecText(repeat.GetProperty("value")),repeat.GetProperty("count").GetInt32()));
        if(value.TryGetProperty("bytes",out var bytes))return bytes.EnumerateArray().Select(x=>x.GetByte()).ToArray();
        throw new ArgumentException("Unknown writer input descriptor.");
    }
    private static object CodecWritersRequest(JsonElement input){
        bool binary=input.GetProperty("mode").GetString()=="binary";
        var type=typeof(DxfDocument).Assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        CodecOutputStream? stream=binary?new CodecOutputStream(input):null;CodecOutputText? text=binary?null:new CodecOutputText(input);
        object? writer=null,constructionError=null;var results=new List<object>();
        object Error(Exception error){while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
            // .NET-owned null/cast/encoding/argument message wording is not the codec contract.
            return new{name=error.GetType().Name,param=(error as ArgumentException)?.ParamName,message=error is IOException||error.GetType()==typeof(Exception)?error.Message:null};}
        try{
            bool isNull=input.TryGetProperty("nullWriter",out var nullWriter)&&nullWriter.GetBoolean();
            if(binary){string kind=input.TryGetProperty("encoding",out var enc)?enc.GetString()!:"strict";
                Encoding encoding=kind=="ascii"?Encoding.ASCII:kind=="latin1"?Encoding.Latin1:new UTF8Encoding(false,kind=="strict");
                writer=Activator.CreateInstance(type,new object?[]{isNull?null:new BinaryWriter(stream!,encoding,true),input.TryGetProperty("legacy",out var legacy)&&legacy.GetBoolean()});
            }else writer=Activator.CreateInstance(type,new object?[]{isNull?null:text});
        }catch(Exception error){constructionError=Error(error);}
        object? Invoke(JsonElement step){string method=step.GetProperty("method").GetString()!;
            if(method=="Position")return type.GetProperty("CurrentPosition")!.GetValue(writer);
            if(method=="Write")return type.GetMethod(method)!.Invoke(writer,new object?[]{step.GetProperty("code").GetInt16(),CodecWriterInput(step.GetProperty("value"))});
            var args=step.TryGetProperty("value",out var value)?new object?[]{CodecWriterInput(value)}:Array.Empty<object?>();
            return type.GetMethod(method,BindingFlags.Instance|BindingFlags.Public)!.Invoke(writer,args);
        }
        Action hook=()=>{if(input.TryGetProperty("hook",out var inner))Invoke(inner);};if(binary)stream!.Hook=hook;else text!.Hook=hook;
        object? State(){if(writer==null)return null;object? position;try{position=type.GetProperty("CurrentPosition")!.GetValue(writer);}catch(Exception error){position=new{error=Error(error)};}
            return new{code=(short)type.GetProperty("Code")!.GetValue(writer)!,value=CodecValue(type.GetProperty("Value")!.GetValue(writer)),position};}
        object Host()=>binary?new{calls=stream!.Calls.ToArray(),output=CodecValue(stream.Bytes),closed=stream.Closed}:new{calls=text!.Calls.ToArray(),output=CodecValue(text.Text.ToString()),closed=text.Closed};
        var initial=State();
        if(constructionError==null)foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? value=null,error=null;try{value=CodecValue(Invoke(step));}catch(Exception e){error=Error(e);}
            results.Add(new{value,error,state=State(),host=Host()});
        }
        return new{constructor=constructionError,initial,results,host=Host()};
    }
}
