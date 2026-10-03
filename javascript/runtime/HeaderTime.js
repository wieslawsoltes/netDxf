// Exact value adapters for the DateTime/TimeSpan fields stored in DXF headers.
// They are not a replacement for the complete System.DateTime/TimeSpan API.
import { CopyValue } from './GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from './Errors.js';
const epochTicks = 621355968000000000n;
const maxTicks = 3155378975999999999n;
const checkTicks = (value,min,max) => {
  if (typeof value !== 'bigint') throw new ArgumentException('Ticks must be a BigInt.', 'ticks');
  if (value < min || value > max) throw new ArgumentOutOfRangeException('ticks',value);
  return value;
};
const pad = (n,size=2) => String(n).padStart(size,'0');
/** Immutable wall-clock ticks, measured from 0001-01-01 in 100 ns units, plus DateTimeKind. */
export class HeaderDateTime {
  #ticks; #kind;
  constructor(ticks=0n,kind=0) {
    this.#ticks=checkTicks(ticks,0n,maxTicks);
    this.#kind=RequireInteger(kind,0,2,'kind');
    Object.freeze(this);
  }
  static get MinValue(){return new HeaderDateTime();}
  static get MaxValue(){return new HeaderDateTime(maxTicks);}
  static FromDate(value,kind=1) {
    if (!(value instanceof Date) || !Number.isFinite(value.getTime())) throw new ArgumentException('A valid Date is required.','value');
    RequireInteger(kind,0,2,'kind');
    let ms=value.getTime();
    if(kind!==1) { // Local/unspecified wall-clock fields, not an implicit UTC conversion.
      const wall=new Date(0);
      wall.setUTCFullYear(value.getFullYear(),value.getMonth(),value.getDate());
      wall.setUTCHours(value.getHours(),value.getMinutes(),value.getSeconds(),value.getMilliseconds());
      ms=wall.getTime();
    }
    return new HeaderDateTime(BigInt(ms)*10000n+epochTicks,kind);
  }
  get Ticks(){return this.#ticks;}
  get Kind(){return this.#kind;}
  // Calendar decomposition is exact through milliseconds; the remaining ticks stay stored.
  get Calendar(){const d=new Date(Number(this.#ticks/10000n)-Number(epochTicks/10000n));
    return [d.getUTCFullYear(),d.getUTCMonth()+1,d.getUTCDate(),d.getUTCHours(),d.getUTCMinutes(),d.getUTCSeconds(),d.getUTCMilliseconds()];}
  get Year(){return this.Calendar[0];} get Month(){return this.Calendar[1];} get Day(){return this.Calendar[2];}
  get Hour(){return this.Calendar[3];} get Minute(){return this.Calendar[4];} get Second(){return this.Calendar[5];}
  get Millisecond(){return this.Calendar[6];}
  Equals(value){return value instanceof HeaderDateTime&&value.Ticks===this.#ticks;}
  [CopyValue](){return this;}
  ToString(){const [y,m,d,h,min,s]=this.Calendar;return `${pad(m)}/${pad(d)}/${pad(y,4)} ${pad(h)}:${pad(min)}:${pad(s)}`;}
}
/** Immutable signed 64-bit duration ticks. Negative values and sub-millisecond units are retained. */
export class HeaderTimeSpan {
  #ticks;
  constructor(ticks=0n){this.#ticks=checkTicks(ticks,-9223372036854775808n,9223372036854775807n);Object.freeze(this);}
  static get Zero(){return new HeaderTimeSpan();}
  get Ticks(){return this.#ticks;}
  Equals(value){return value instanceof HeaderTimeSpan&&value.Ticks===this.#ticks;}
  [CopyValue](){return this;}
  ToString(){
    const negative=this.#ticks<0n;let ticks=negative?-this.#ticks:this.#ticks;
    const days=ticks/864000000000n;ticks%=864000000000n;
    const h=ticks/36000000000n;ticks%=36000000000n;
    const m=ticks/600000000n;ticks%=600000000n;
    const s=ticks/10000000n,fraction=ticks%10000000n;
    return `${negative?'-':''}${days?days+'.':''}${pad(h)}:${pad(m)}:${pad(s)}${fraction?'.'+pad(fraction,7):''}`;
  }
}
// Browser clocks have millisecond resolution and cannot expose an operating-system username.
// Applications can supply a host; the explicit Node entry installs os.userInfo().username.
const browserHost=Object.freeze({UserName:()=>'',Now:()=>HeaderDateTime.FromDate(new Date(),2),UtcNow:()=>HeaderDateTime.FromDate(new Date(),1)});
let host=browserHost;
export function GetHeaderEnvironment(){return host;}
export function SetHeaderEnvironment(value){
  if(value===null){host=browserHost;return;}
  if(!value||!['UserName','Now','UtcNow'].every(key=>typeof value[key]==='function'))
    throw new ArgumentException('A header host must provide UserName, Now and UtcNow functions.','value');
  host=Object.freeze({UserName:()=>value.UserName(),Now:()=>value.Now(),UtcNow:()=>value.UtcNow()});
}
