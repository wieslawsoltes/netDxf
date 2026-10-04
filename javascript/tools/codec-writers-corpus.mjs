// Deterministic writer inputs. No serialized outputs or native errors are embedded.
import { doubleBits } from './wire.mjs';
const real=value=>({double:doubleBits(value)}),short=int16=>({int16}),int=int32=>({int32}),bytes=value=>({bytes:value}),utf16=value=>({utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))});
const write=(code,value)=>({method:'Write',code,value});
const observe=[{method:'ToString'},{method:'Position'}];
const groups=[['s',[0,1,5,9,100,101,102,105,300,309,320,329,330,369,390,399,410,419,430,439,470,479,480,481,1000,1003,1005,1009]],
  ['d',[10,39,40,59,110,119,120,129,130,139,140,149,210,239,460,469,1010,1059]],['i16',[60,79,170,179,270,279,280,289,370,379,380,389,400,409,1060,1070]],['i32',[90,99,420,429,440,459,1071]],['i64',[160,169]],['b',[290,299]],['data',[310,319,1004]]];
export function codecWritersCorpus(){
  const cases=[],add=(name,mode,steps,extra={})=>cases.push({name:'codec-writers/'+mode+'/'+name,request:{op:'codec-writers',mode,steps,...extra}});
  for(const mode of ['text','binary'])for(const legacy of mode==='binary'?[false,true]:[false]){
    const values={s:['','plain','00aa','Ω😀'],d:[0,-0,1.5,Math.PI,1e-300,5e-324,Number.MAX_VALUE,NaN,Infinity,-Infinity].map(real),i16:[short(-32768),short(32767)],i32:[int(-2147483648),int(2147483647)],i64:[{int64:'-9223372036854775808'},{int64:'9223372036854775807'}],b:[false,true],data:[bytes([]),bytes([0,127,255]),bytes(Array.from({length:255},(_,i)=>i))]};
    for(const [type,codes] of groups)for(const code of codes)add('group/'+legacy+'/'+code,mode,values[type].flatMap(value=>[write(code,value),...observe]),{legacy});
    for(const code of [-32768,-1,80,89,103,106,109,150,159,180,209,240,269,482,998,999,1072,32767])add('unknown/'+legacy+'/'+code,mode,[write(70,short(42)),write(code,'unused'),...observe,write(1,'after')],{legacy});
    for(const seekable of mode==='binary'?[false,true]:[true])for(const failAt of [1,2,3,4,5,6,7,8])add(`failure/${legacy}/${seekable}/${failAt}`,mode,[write(1,'before'),write(70,short(1234)),write(310,bytes([1,2,3])),write(40,real(Math.PI)),{method:'Flush'},...observe],{legacy,seekable,failAt,partial:1});
    for(const failFlush of [1,2,3,4])add('flush/'+legacy+'/'+failFlush,mode,[write(1,'value'),{method:'Flush'},...observe,write(40,real(1.25))],{legacy,failFlush});
    for(const hookAt of [2,3,4])for(const hook of [write(90,int(0x12345678)),write(1,'reentrant'),{method:'Flush'}])add('callback/'+legacy+'/'+hookAt+'/'+hook.method+'/'+(hook.code??0),mode,[write(70,short(4660)),write(40,real(2.25)),write(1,'outer'),...observe],{legacy,hookAt,hook});
    for(const method of ['WriteByte','WriteShort','WriteInt','WriteLong','WriteBool','WriteDouble','WriteString','WriteBytes']){
      const val={WriteByte:{byte:255},WriteShort:short(-123),WriteInt:int(1234567),WriteLong:{int64:'9223372036854775807'},WriteBool:true,WriteDouble:real(-0),WriteString:'Ω\0after',WriteBytes:bytes([1,2,255])}[method];
      add('direct/'+legacy+'/'+method,mode,[{method,value:val},...observe,write(1,'normal'),{method,value:val},...observe],{legacy});
    }
    for(const culture of ['', 'en-US','pl-PL','tr-TR'])add('culture/'+legacy+'/'+culture,mode,[write(40,real(12.5)),...observe,write(290,true),...observe,write(310,bytes([1,2,3])),...observe],{legacy,culture});
  }
  for(const mode of ['text','binary']){
    add('null-writer',mode,[write(1,'x'),{method:'Flush'},...observe],{nullWriter:true});
    for(const method of ['WriteString','WriteBytes'])add('null-direct/'+method,mode,[{method,value:null},...observe]);
    for(const value of [null,'wrong',bytes(Array(256).fill(1)),bytes(Array(4096).fill(2))])if(mode==='binary')add('chunk-preflight/'+JSON.stringify(value).slice(0,80),mode,[write(70,short(42)),write(310,value),...observe,write(1,'after')]);
    if(mode==='text')add('custom-newline',mode,[write(40,real(1.5)),write(1,'line1\nline2')],{newline:'\r\n'});
  }
  for(const encoding of ['strict','replacement','ascii','latin1'])for(const value of encoding==='ascii'?['ASCII','']:encoding==='latin1'?['éÿ','']:['','Ω😀','\uFEFFvalue',utf16('\ud800'),utf16('left\udc00right')])add('encoding/'+encoding+'/'+JSON.stringify(value),'binary',[{method:'WriteString',value},write(70,short(42)),...observe],{encoding});
  for(const [value,count] of [['A',21844],['A',21845],['A',65536],['A',65537],['Ω',40000],['😀',20000],['😀',40000]])add('large-string/'+value+'/'+count,'binary',[{method:'WriteString',value:{repeat:{value,count}}},...observe]);
  for(const seekable of [false,true])add('position-errors/'+seekable,'binary',[write(80,'unused'),write(999,'unused'),...observe],{seekable});
  for(const mode of ['text','binary'])for(const hookAt of [mode==='text'?1:2])for(const code of [80,999])add('unknown-reentrant/'+code,mode,[write(code,'unused'),...observe],{hookAt,hook:write(70,short(42))});
  for(const prefix of [65530,65533,65535,65536,70000])for(const malformed of [false,true]) {
    const value='A'.repeat(prefix)+(malformed?'\ud800':'Ω😀')+'tail';
    add('boundary/'+prefix+'/'+malformed,'binary',[{method:'WriteString',value:utf16(value)},write(70,short(42))]);
  }
  let random=0x51a11ace;const next=()=>random=(Math.imul(random,1664525)+1013904223)>>>0;
  for(let run=0;run<32;run++)for(const mode of ['text','binary']){const steps=[];for(let i=0;i<16;i++){const code=[40,70,90,1][next()%4];const value=code===40?real((next()/4294967296-.5)*1e200):code===70?short((next()%65536)-32768):code===90?int(next()|0):'S'+next();steps.push(write(code,value));}steps.push(...observe);add('random/'+run,mode,steps);}
  return cases;
}
