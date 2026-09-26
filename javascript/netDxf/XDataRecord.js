// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { XDataCode } from './XDataCode.js';
import { GeneralNumber } from '../runtime/Numerics.js';
import { ArgumentNullException, ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../runtime/Errors.js';
const names=new Map(Object.entries(XDataCode).map(([name,code])=>[code,name]));
const realCodes=new Set([1010,1020,1030,1011,1021,1031,1012,1022,1032,1013,1023,1033,1040,1041,1042]);
function isHexInt64(value){
  // NumberStyles.HexNumber permits ASCII whitespace and leading zero digits. It is not the
  // raw DxfTag handle contract, which intentionally rejects whitespace and long lexical handles.
  const match=/^[\t\n\v\f\r ]*([0-9a-fA-F]+)[\t\n\v\f\r ]*\0*$/.exec(value);
  if(!match)return false;const significant=match[1].replace(/^0+/,'');return significant.length<=16;
}
export class XDataRecord {
  #code;#value;
  constructor(code,value){
    if(value==null)throw new ArgumentNullException('value');
    RequireInteger(code,-2147483648,2147483647,'code');
    const invalid=message=>{throw new ArgumentException(message,'value');};
    switch(code){
      case XDataCode.AppReg:invalid('An application registry cannot be an extended data record.');break;
      case XDataCode.BinaryData:
        if(!(value instanceof Uint8Array))invalid('The value of XDataCode.BinaryData must be a byte array.');
        if(value.length>127)throw new ArgumentOutOfRangeException('value',value,'The maximum binary XData record length is 127.');break;
      case XDataCode.ControlString:
        if(value!=='{'&&value!=='}')invalid('ControlString must be { or }.');break;
      case XDataCode.DatabaseHandle:
        if(typeof value!=='string'||!isHexInt64(value))invalid('DatabaseHandle must be a hexadecimal number.');break;
      case XDataCode.Int16:
        if(!Number.isInteger(value)||value<-32768||value>32767)invalid('Int16 requires its mapped Number and range.');break;
      case XDataCode.Int32:
        if(!Number.isInteger(value)||value<-2147483648||value>2147483647)invalid('Int32 requires its mapped Number and range.');break;
      case XDataCode.String:case XDataCode.LayerName:
        if(typeof value!=='string')invalid('String and LayerName require strings.');
        if(value.length>255)throw new ArgumentOutOfRangeException('value',value,'The maximum string XData length is 255 UTF-16 code units.');break;
      default:
        if(realCodes.has(code)&&typeof value!=='number')invalid('Real XData requires a mapped double.');
        // The source accepts non-null payloads for unknown enum codes. Do not reject them here.
    }
    this.#code=code;this.#value=value;
  }
  static get OpenControlString(){return new XDataRecord(XDataCode.ControlString,'{');}
  static get CloseControlString(){return new XDataRecord(XDataCode.ControlString,'}');}
  get Code(){return this.#code;}
  get Value(){return this.#value;}
  ToString(){
    const value=this.#value;
    const text=typeof value==='number'?GeneralNumber(value):typeof value==='boolean'?(value?'True':'False'):
      value instanceof Uint8Array?'System.Byte[]':typeof value==='string'?value:typeof value.ToString==='function'?value.ToString():'System.Object';
    return `${names.get(this.Code)??String(this.Code)} - ${text}`;
  }
}
