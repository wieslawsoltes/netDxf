import { Body, Solid3D, AcisEntity, AcisSatChunk } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterAcisSatTests(){Run('acis-sat/model-codec-transaction',AcisSatModel);}
function* repeat(value,count){for(let i=0;i<count;i++)yield value;}
function* AcisFailingEnumeration(){yield new AcisSatChunk(1,'valid');throw new InvalidOperationException('caller enumeration failed');}
export function AcisSatModel(){
  const entity=new Body(),printable=String.fromCharCode(...Array.from({length:95},(_,i)=>32+i));
  const lines=[printable,'  leading and trailing  ','','0'.repeat(254)+'A'+'C'.repeat(260),'700 0 1 0'];
  entity.SetSatLines(lines);Equal(lines,[...entity.SatLines],'SAT printable codec roundtrip');
  Check([...entity.EncodedSatChunks].some(c=>c.GroupCode===3),'SAT long line not chunked');
  Check([...entity.EncodedSatChunks].some(c=>c.Text.endsWith('^')),'A escape did not cross chunk boundary');
  const snapshot=entity.EncodedSatChunks,decodedSnapshot=entity.SatLines;
  Throws(ArgumentException,()=>entity.SetSatLines(['valid','invalid\nline']));
  Throws(ArgumentException,()=>entity.SetSatLines(['valid','café']));
  Throws(ArgumentException,()=>entity.SetSatLines(['valid',null]));
  Throws(ArgumentNullException,()=>entity.SetSatLines(null));Throws(ArgumentNullException,()=>entity.SetEncodedSatChunks(null));
  Throws(ArgumentException,()=>entity.SetEncodedSatChunks([new AcisSatChunk(3,'orphan')]));
  Throws(ArgumentException,()=>entity.SetEncodedSatChunks([new AcisSatChunk(1,'valid'),null]));
  Throws(ArgumentException,()=>entity.SetEncodedSatChunks([new AcisSatChunk(1,'^')]));
  Throws(ArgumentException,()=>entity.SetEncodedSatChunks([new AcisSatChunk(1,'^x')]));
  Throws(ArgumentOutOfRangeException,()=>new AcisSatChunk(2,'x'));Throws(ArgumentNullException,()=>new AcisSatChunk(1,null));
  Throws(ArgumentOutOfRangeException,()=>new AcisSatChunk(1,'x'.repeat(256)));
  Throws(ArgumentException,()=>new AcisSatChunk(1,'\0'));Throws(ArgumentException,()=>new AcisSatChunk(1,'\x7f'));
  Throws(ArgumentOutOfRangeException,()=>entity.SetSatLines(['A'.repeat(AcisEntity.MaximumSatLineCharacters)]));
  Throws(ArgumentOutOfRangeException,()=>entity.SetEncodedSatChunks(repeat(new AcisSatChunk(1,''),AcisEntity.MaximumSatChunks+1)));
  Throws(ArgumentOutOfRangeException,()=>entity.SetEncodedSatChunks((function*(){yield new AcisSatChunk(1,'x');yield* repeat(new AcisSatChunk(3,'x'.repeat(255)),Math.floor(AcisEntity.MaximumSatLineCharacters/255)+1);})()));
  Throws(InvalidOperationException,()=>entity.SetEncodedSatChunks(AcisFailingEnumeration()));
  Check(snapshot===entity.EncodedSatChunks&&decodedSnapshot===entity.SatLines,'Failed setters published partial data');
  Throws(NotSupportedException,()=>snapshot.set_Item(0,new AcisSatChunk(1,'')));
  const chunks=[new AcisSatChunk(1,'\\U+0041'),new AcisSatChunk(3,' '),new AcisSatChunk(1,'^'),new AcisSatChunk(3,' ')];
  entity.SetEncodedSatChunks(chunks);chunks.length=0;Equal(4,entity.EncodedSatChunks.Count,'SAT setter retained mutable collection');
  Equal(lines,[...decodedSnapshot],'Old SAT snapshot changed');entity.SetSatLines(['0'.repeat(AcisEntity.MaximumSatLineCharacters)]);
  Equal(AcisEntity.MaximumSatLineCharacters,entity.SatLines.get_Item(0).length,'Legal SAT line boundary rejected');
  entity.SetEncodedSatChunks(repeat(new AcisSatChunk(1,''),AcisEntity.MaximumSatChunks));Equal(AcisEntity.MaximumSatChunks,entity.EncodedSatChunks.Count,'Legal chunk count rejected');
  entity.SetSatLines([]);Equal(0,entity.EncodedSatChunks.Count,'SAT clear');
  const solid=new Solid3D();solid.HistoryHandle='0';Throws(NotSupportedException,()=>{solid.HistoryHandle='FF';});Throws(NotSupportedException,()=>{solid.HistoryHandle='';});
  Equal('0',solid.HistoryHandle,'Failed history setter mutation');solid.HistoryHandle=null;
}
