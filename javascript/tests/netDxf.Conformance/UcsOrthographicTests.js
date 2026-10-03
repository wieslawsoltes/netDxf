import { UcsElevationFixture, UcsCodeReader, UcsCodeWriter } from './UcsElevationTests.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
// Complete pinned UcsOrthographicTests.cs, retaining the original detached-model test.
import { UCS,Vector3,UcsOrthographicType,DxfDocument,MemoryStream } from '../../index.js';
import { NotSupportedException,ArgumentOutOfRangeException,InvalidDataException } from '../../runtime/Errors.js';
import { Run,Check,Equal,Near,Throws,SupportedVersions,VersionName,BooleanName } from './TestHarness.js';
export function RegisterUcsOrthographicTests(){Run('ucs/orthographic/model',UcsOrthographicModelTests);for(const v of SupportedVersions)for(const b of [false,true]){const suffix=`${VersionName(v)}/${BooleanName(b)}`;
  Run(`ucs/orthographic/preserve/${suffix}`,()=>UcsOrthographicRetention(v,b));Run(`ucs/orthographic/edit/${suffix}`,()=>UcsOrthographicEdit(v,b));Run(`ucs/orthographic/absent/${suffix}`,()=>UcsOrthographicAbsent(v,b));
  for(const invalid of ['zero-type','negative-type','large-type','unpaired','duplicate-coordinate','missing-x','missing-y','missing-z','missing-all','duplicate-type','interrupted-pair'])Run(`ucs/orthographic/invalid/${suffix}/${invalid}`,()=>UcsOrthographicInvalid(v,b,invalid));
}}
    export function UcsOrthographicModelTests()
    {
        for (const ucs of [ new UCS("Default"), new UCS("Axes", new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitY),
            UCS.FromNormal("Normal", Vector3.Zero, Vector3.UnitZ) ])
        {
            Equal(0, ucs.OrthographicOrigins.Count, "new UCS invented overrides");
            Check(ucs.OrthographicOrigins === ucs.OrthographicOrigins, "Read-only view is not stable.");
            for (let type = 1; type <= 6; type++)
            {
                const key = type;
                const box = {}; Check(!ucs.TryGetOrthographicOrigin(key, box) && Vector3.Equals(box.value, Vector3.Zero), "Missing override was invented.");
                ucs.SetOrthographicOrigin(key, OrthoPoint(type));
                Check(ucs.TryGetOrthographicOrigin(key, box), "Stored override missing.");
                Equal(OrthoPoint(type), box.value, "override value");
                ucs.SetOrthographicOrigin(key, Vector3.Zero);
                Equal(Vector3.Zero, ucs.OrthographicOrigins.get_Item(key), "explicit zero override");
                Check(ucs.RemoveOrthographicOrigin(key), "Override not removed.");
                Check(!ucs.RemoveOrthographicOrigin(key), "Absent override reported as removed.");
            }
            const dictionary = ucs.OrthographicOrigins;
            Throws(NotSupportedException,() => dictionary.Add(UcsOrthographicType.Top, Vector3.Zero));
            Equal(0, ucs.OrthographicOrigins.Count, "Read-only mutation changed the model.");
            for (const invalid of [ -1, 0, 7, 32767 ])
            {
                const type = invalid;
                Throws(ArgumentOutOfRangeException,() => ucs.SetOrthographicOrigin(type, Vector3.Zero));
                Throws(ArgumentOutOfRangeException,() => ucs.TryGetOrthographicOrigin(type, {}));
                Throws(ArgumentOutOfRangeException,() => ucs.RemoveOrthographicOrigin(type));
            }
            ucs.SetOrthographicOrigin(UcsOrthographicType.Top, OrthoPoint(1));
            for (const invalid of [ NaN, Infinity, -Infinity ])
                for (const point of [ new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) ])
                {
                    Throws(ArgumentOutOfRangeException,() => ucs.SetOrthographicOrigin(UcsOrthographicType.Top, point));
                    Equal(OrthoPoint(1), ucs.OrthographicOrigins.get_Item(UcsOrthographicType.Top), "Invalid assignment changed stored origin.");
                }
        }
        const original = new UCS("Original",new Vector3(3,4,5),Vector3.UnitY,Vector3.Negate(Vector3.UnitX)); original.Elevation=-2.5;
        for (let type = 1; type <= 6; ++type) original.SetOrthographicOrigin(type, OrthoPoint(type));
        const copy = original.Clone("Copy");
        Equal(6, copy.OrthographicOrigins.Count, "clone override count");
        Equal(original.Origin, copy.Origin, "clone base origin");
        Equal(original.GetTransformation(), copy.GetTransformation(), "clone axes");
        Near(original.Elevation, copy.Elevation, "clone elevation");
        for (const pair of original.OrthographicOrigins) Equal(pair.Value, copy.OrthographicOrigins.get_Item(pair.Key), "clone override");
        copy.RemoveOrthographicOrigin(UcsOrthographicType.Left);
        copy.SetOrthographicOrigin(UcsOrthographicType.Right, Vector3.Zero);
        Equal(6, original.OrthographicOrigins.Count, "clone removal affected source");
        Equal(OrthoPoint(6), original.OrthographicOrigins.get_Item(UcsOrthographicType.Right), "clone edit affected source");
    }


const OrthoPoint = type => new Vector3(type * 1.25, -type * 2.5, type * 3.75);

export function UcsOrthographicEdit(version,binary){const document=new DxfDocument(version),ucs=new UCS('Editable',new Vector3(100,-200,300),Vector3.UnitY,Vector3.Negate(Vector3.UnitX));ucs.Elevation=12.5;ucs.SetOrthographicOrigin(UcsOrthographicType.Left,Vector3.Zero);ucs.SetOrthographicOrigin(UcsOrthographicType.Right,OrthoPoint(6));ucs.SetOrthographicOrigin(UcsOrthographicType.Top,OrthoPoint(1));ucs.RemoveOrthographicOrigin(UcsOrthographicType.Right);document.UCSs.Add(ucs);const output=new MemoryStream(),second=new MemoryStream();try{Check(document.Save(output,binary),'Edited UCS failed to save.');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Edited UCS failed to load.');const copy=loaded.UCSs.get_Item(ucs.Name),point={};Equal(2,copy.OrthographicOrigins.Count,'edited override count');Check(copy.TryGetOrthographicOrigin(UcsOrthographicType.Left,point),'Explicit zero origin was omitted.');Equal(Vector3.Zero,point.value,'explicit zero origin');Check(!copy.TryGetOrthographicOrigin(UcsOrthographicType.Right,{}),'Removed origin was regenerated.');Equal(OrthoPoint(1),copy.OrthographicOrigins.get_Item(UcsOrthographicType.Top),'saved override');Equal(ucs.Origin,copy.Origin,'override moved base origin');Equal(ucs.XAxis,copy.XAxis,'override rotated axes');Near(12.5,copy.Elevation,'override changed elevation');Check(loaded.GetObjectByHandle(copy.Handle)===copy,'UCS handle ownership broken.');copy.SetOrthographicOrigin(UcsOrthographicType.Top,OrthoPoint(4));Check(loaded.Save(second,!binary),'Cross-transport save failed.');second.Position=0;const reloaded=DxfDocument.Load(second);Check(reloaded!==null,'Cross-transport load failed.');Equal(OrthoPoint(4),reloaded.UCSs.get_Item(ucs.Name).OrthographicOrigins.get_Item(UcsOrthographicType.Top),'edit then cross-transport reload');}finally{second.Dispose();output.Dispose();}}
export function UcsOrthographicAbsent(version,binary){const input=UcsOrthographicFixture(version,binary,[]),output=new MemoryStream();try{const loaded=DxfDocument.Load(input);Check(loaded!==null,'No-override fixture failed.');Equal(0,loaded.UCSs.get_Item('Fixture').OrthographicOrigins.Count,'absent origins were invented');Check(loaded.Save(output,binary),'No-override save failed.');output.Position=0;Equal(0,ReadUcsOrthographicTags(output,binary).length,'writer invented origin overrides');}finally{output.Dispose();input.Dispose();}}
export function UcsOrthographicInvalid(version,binary,kind){const tags=[[71,1],[13,1],[23,2],[33,3]];switch(kind){case 'zero-type':tags[0]=[71,0];break;case 'negative-type':tags[0]=[71,-1];break;case 'large-type':tags[0]=[71,7];break;case 'unpaired':tags.splice(0,1);break;case 'duplicate-coordinate':tags.splice(2,0,[13,4]);break;case 'missing-x':tags.splice(1,1);break;case 'missing-y':tags.splice(2,1);break;case 'missing-z':tags.splice(3,1);break;case 'missing-all':tags.splice(1,3);break;case 'duplicate-type':tags.push(...tags.slice());break;case 'interrupted-pair':tags.splice(2,0,[71,2]);break;default:throw new ArgumentOutOfRangeException('kind');}const input=UcsOrthographicFixture(version,binary,tags);try{if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'Malformed orthographic origin pair was silently accepted.');Check(input.CanRead,'Rejected fixture closed caller input.');}finally{input.Dispose();}}
export function UcsOrthographicRetention(version,binary){const pairs=[];for(let type=6;type>=1;type--){const point=OrthoPoint(type);pairs.push([71,type],[33,point.Z]);if(!binary)pairs.push([999,'comment within origin pair']);pairs.push([13,point.X],[146,-17.625],[23,point.Y]);}pairs.push([1001,'ORTHOGRAPHIC_TEST'],[1000,'origin metadata']);const input=UcsOrthographicFixture(version,binary,pairs),output=new MemoryStream();try{const loaded=DxfDocument.Load(input);Check(loaded!==null,'Orthographic UCS fixture failed to load.');const ucs=loaded.UCSs.get_Item('Fixture');Equal(new Vector3(1,2,3),ucs.Origin,'base origin changed');Near(-17.625,ucs.Elevation,'base elevation changed');Equal('origin metadata',ucs.XData.get_Item('ORTHOGRAPHIC_TEST').XDataRecord.get_Item(0).Value,'paired data consumed XData');Check(loaded.Save(output,binary),'Orthographic UCS fixture failed to save.');output.Position=0;const written=ReadUcsOrthographicTags(output,binary);Equal(24,written.length,'six UCS orthographic origin pairs were discarded');for(let type=1;type<=6;type++){const offset=(type-1)*4,point=OrthoPoint(type);Equal([71,type],written[offset],'canonical pair order');Equal([13,point.X],written[offset+1],'origin X');Equal([23,point.Y],written[offset+2],'origin Y');Equal([33,point.Z],written[offset+3],'origin Z');}}finally{output.Dispose();input.Dispose();}}
export function UcsOrthographicFixture(version,binary,pairs){const template=UcsElevationFixture(version,binary,null),output=new MemoryStream(),writer=UcsCodeWriter(output,binary);try{const reader=UcsCodeReader(template,binary);while(true){reader.Next();const code=reader.Code,value=reader.Value;if(code===0&&value==='ENDTAB')for(const [c,v]of pairs)writer.Write(c,v);writer.Write(code,value);if(code===0&&value==='EOF')break;}writer.Flush();output.Position=0;return output;}finally{template.Dispose();}}
export function ReadUcsOrthographicTags(input,binary){const reader=UcsCodeReader(input,binary),tags=[];let record='';while(true){reader.Next();const code=reader.Code;if(code===0){record=reader.ReadString();if(record==='EOF')return tags;}if(record==='UCS'&&[71,13,23,33].includes(code))tags.push([code,reader.Value]);}}
