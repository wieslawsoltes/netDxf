// Port of pinned OutputSettingsTests.cs. Original names, assertions and fixtures retained.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { sourceRoot } from '../../tools/dotnet.mjs';
import { DxfDocument, DxfVersion, DxfDictionary, DxfDictionaryVariable, DxfXRecord, DxfIdBuffer,
  PlotSettings, PaperMargin, PlotPaperUnits, PlotRotation, PlotType, ShadePlotMode, ShadePlotResolutionMode,
  Layout, MemoryStream, DxfRawDocument, DxfRawHandleIndex, DxfTag, Line, Vector2, Vector3,
  TextStyle, Block, AttributeDefinition, Insert, ApplicationRegistry, XData, XDataRecord, XDataCode } from '../../node-entry.js';
import { PeekDocumentObjects } from '../../netDxf/DxfDocument.Objects.js';
import { ArgumentException, ArgumentOutOfRangeException, InvalidOperationException, InvalidDataException, FormatException, IOException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
const A=Array.from,T=(c,v)=>new DxfTag(c,v);
export function RegisterOutputSettingsTests() {
  for(const v of SupportedVersions)for(const b of [false,true])for(const [name,fn]of [['authored',OutputSettingsAuthored],['independent',OutputSettingsIndependent],['stored-factor',OutputSettingsStoredFactor]])
    Run(`output-settings/${name}/${VersionName(v)}/${BooleanName(b)}`,()=>fn(v,b));
  for(const [name,fn]of [['clone-references-and-failures',OutputSettingsClone],['clone-callback-recheck',OutputSettingsCloneCallbacks],['validation-and-compatibility',OutputSettingsValidation],['shade-reference-failure-and-retry',OutputSettingsShadeRetry],['shade-reference-profile-promotion',OutputSettingsShadeProfiles],['graphical-shade-reference-rejection',OutputSettingsGraphicalShade],['fresh-invalid-preflight',OutputSettingsFreshPreflight],['clone-exact-object-identity',OutputSettingsCloneIdentity]])
    Run('output-settings/'+name,fn);
  for(const kind of ['PLOTSETTINGS','LAYOUT','WIPEOUTVARIABLES'])for(let i=0;i<(kind==='WIPEOUTVARIABLES'?2:kind==='LAYOUT'?11:9);i++)Run(`output-settings/malformed/${kind}/${i}`,()=>OutputSettingsMalformed(kind,i));
}
function OutputPayload(scale) {
  const plot=new PlotSettings();Object.assign(plot,{PageSetupName:'payload',PlotterName:String.raw`Printer\U+0041 Żółć`,PaperSizeName:'Custom 東京',ViewName:'',CurrentStyleSheet:String.raw`C:\plot\style.ctb`,
    PaperMargin:new PaperMargin(1.25,2.5,3.75,4.125),PaperSize:new Vector2(310.5,207.25),Origin:new Vector2(-7.75,8.125),WindowBottomLeft:new Vector2(-11.25,-22.5),WindowUpRight:new Vector2(33.75,44.125),
    PrintScaleNumerator:2.5,PrintScaleDenominator:17.75,Flags:687,PaperUnits:PlotPaperUnits.Milimeters,PaperRotation:PlotRotation.Degrees270,PlotType:PlotType.Window,StandardScaleType:scale,StandardScaleFactor:.03125,
    ShadePlotMode:ShadePlotMode.Hidden,ShadePlotResolutionMode:ShadePlotResolutionMode.Custom,ShadePlotDPI:777,PaperImageOrigin:new Vector2(-.625,1.875)});return plot;
}
const fixture=v=>path.join(sourceRoot,'tests/fixtures/output-settings',`independent-output-settings-R${VersionName(v).slice(7)}.dxf`);
const named=doc=>doc.NamedObjects.get_Item('ACAD_PLOTSETTINGS');
function saveRejected(doc,stream) {
  let rejected;try{rejected=!doc.Save(stream);}catch(e){if(!(e instanceof InvalidOperationException||e instanceof InvalidDataException))throw e;rejected=true;}
  Check(rejected,'Invalid document saved');
}
function replaceRecord(raw,handle,edit) {
  // Original ObjectStoreReplaceRecord shared helper, with the same physical lookup.
  const record=OleSingle(DxfRawHandleIndex.Create(raw).FindDefinitions(handle)).Record;
  return raw.WithTags([...A(raw.Tags).slice(0,record.StartTagIndex),...edit(A(record.Tags)),...A(raw.Tags).slice(record.EndTagIndex)]);
}
function OutputSettingsAuthored(version,binary) {
  const doc=new DxfDocument(version);doc.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));
  for(let code=0;code<=32;code++)doc.Objects.AddPlotSettings('User-'+String(code).padStart(2,'0'),OutputPayload(code));
  const layout=doc.Layouts.Add(new Layout('UserSheet'));layout.PlotSettings=OutputPayload(25);const wipe=doc.Objects.SetWipeoutVariables(true),handle=wipe.Handle;
  for(const frame of [false,true]) {
    Check(doc.Objects.SetWipeoutVariables(frame)===wipe,'Wipeout edit changed identity');const output=new MemoryStream();
    try{Check(doc.Save(output,binary),'output settings save');if(frame)OleWriteArtifact(`output-settings-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Output settings load failed');const dictionary=named(loaded);Equal(33,dictionary.Count,'named page count');
      for(let code=0;code<=32;code++) {const item=dictionary.get_Item('User-'+String(code).padStart(2,'0')),plot=item.Settings;Equal(code,plot.StandardScaleType,'all standard scale codes');Equal(code===0,plot.ScaleToFit,'legacy fit predicate');Equal(.03125,plot.StandardScaleFactor,'stored147 independent ratio');Equal(new Vector2(-11.25,-22.5),plot.WindowBottomLeft,'lower-left window');Equal(new Vector2(33.75,44.125),plot.WindowUpRight,'upper-right window');Equal(String.raw`Printer\U+0041 Żółć`,plot.PlotterName,'literal printer');Check(item.PersistentReactors.Contains(dictionary),'page owner reactor');}
      const embedded=loaded.Layouts.get_Item('UserSheet').PlotSettings;Equal(25,embedded.StandardScaleType,'embedded75');Equal(.03125,embedded.StandardScaleFactor,'embedded147');Equal(new Vector2(-11.25,-22.5),embedded.WindowBottomLeft,'embedded lower corner');const result=loaded.Objects.GetWipeoutVariables();Check(result!==null&&result.DisplayFrame===frame&&result.Handle===handle,'wipeout root flag/identity');Equal(0,loaded.Objects.Validate().Count,'loaded output graph');
    }finally{output.Dispose();}
  }
}
function OutputSettingsIndependent(version,binary) {
  const file=fixture(version),manifest=JSON.parse(fs.readFileSync(path.join(sourceRoot,'tests/fixtures/output-settings/manifest.json'),'utf8')),expected=OleSingle(manifest.fixtures.filter(f=>f.file===path.basename(file))),bytes=fs.readFileSync(file);
  Equal(expected.sha256,createHash('sha256').update(bytes).digest('hex'),'external producer hash');const input=new MemoryStream(new Uint8Array(bytes));
  try{const doc=DxfDocument.Load(input);Check(doc!==null,'Independent page load failed');const dictionary=named(doc);Equal(33,dictionary.Count,'independent page count');
    for(let c=0;c<=32;c++){const page=dictionary.get_Item('Independent-'+String(c).padStart(2,'0'));Equal(c,page.Settings.StandardScaleType,'independent75');Equal(.03125,page.Settings.StandardScaleFactor,'independent147');Equal(new Vector2(-11.25,-22.5),page.Settings.WindowBottomLeft,'independent window LL');Equal(new Vector2(33.75,44.125),page.Settings.WindowUpRight,'independent window UR');}
    if(expected.shade_handle!==null){const target=doc.GetObjectByHandle(expected.shade_handle);Check(target?.CodeName==='VISUALSTYLE','independent actual shade target');Check(doc.Layouts.get_Item('IndependentSheet').PlotSettings.ShadePlotObject===target&&dictionary.get_Item('Independent-07').Settings.ShadePlotObject===target,'deferred333 resolution');}
    Equal(0,doc.Objects.Validate().Count,'independent graph');const output=new MemoryStream();try{Check(doc.Save(output,binary),'independent page save');OleWriteArtifact(`independent-output-settings-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());}finally{output.Dispose();}
  }finally{input.Dispose();}
}
function OutputSettingsClone() {
  const source=DxfDocument.Load(fixture(DxfVersion.AutoCad2018)),target=DxfDocument.Load(fixture(DxfVersion.AutoCad2018)),original=named(source).get_Item('Independent-07'),destination=named(target),before=target.Objects.Items.Count,seed=target.DrawingVariables.HandleSeed;
  Throws(InvalidOperationException,()=>target.Objects.CloneObject(original,destination,'MISSING'));Equal(before,target.Objects.Items.Count,'failed clone registration');Equal(seed,target.DrawingVariables.HandleSeed,'failed clone seed');
  const registry=original.XData.get_Item('QA_OUTPUT_SETTINGS').ApplicationRegistry,owner=registry.Owner,handle=registry.Handle,replacement=target.GetObjectByHandle(original.Settings.ShadePlotObject.Handle);
  const clone=target.Objects.CloneObject(original,destination,'COPY',new Map([[original.Settings.ShadePlotObject,replacement]]));
  Check(clone.Settings.ShadePlotObject===replacement,'clone shade mapping');Check(clone.Owner===destination&&clone.PersistentReactors.Contains(destination),'clone owner mapping');Equal(destination.Handle,clone.XData.get_Item('QA_OUTPUT_SETTINGS').XDataRecord.get_Item(1).Value,'clone owner XData mapping');clone.Settings.WindowBottomLeft=new Vector2(99,88);Equal(new Vector2(-11.25,-22.5),original.Settings.WindowBottomLeft,'clone payload independence');
  for(const b of [false,true]){const out=new MemoryStream();try{Check(target.Save(out,b),'clone save');out.Position=0;Check(DxfDocument.Load(out)!==null,'clone reload');}finally{out.Dispose();}}
  Check(registry.Owner===owner&&registry.Handle===handle&&source.GetObjectByHandle(handle)===registry,'clone transferred source registry');Throws(ArgumentException,()=>target.Objects.CloneObject(original,destination,'COPY'));
  const wipe=source.Objects.GetWipeoutVariables(),app=new DxfDictionary();target.NamedObjects.Add('APP_COPY',app);const copied=target.Objects.CloneObject(wipe,app,'FRAME');Equal(wipe.DisplayFrame,copied.DisplayFrame,'single object scalar clone');Check(copied.PersistentReactors.Contains(app),'single object owner reactor mapping');
}
function OutputSettingsCloneCallbacks() {
  const source=new DxfDocument(),item=source.Objects.SetWipeoutVariables(true),target=new DxfDocument(),installed=new DxfDictionaryVariable();let after=0,seed='';
  const callbacks={*[Symbol.iterator](){target.NamedObjects.Add('COPY',installed);after=target.Objects.Items.Count;seed=target.DrawingVariables.HandleSeed;}};
  Throws(ArgumentException,()=>target.Objects.CloneObject(item,target.NamedObjects,'COPY',callbacks));Equal(after,target.Objects.Items.Count,'callback collision registration');Equal(seed,target.DrawingVariables.HandleSeed,'callback collision allocation');Check(target.NamedObjects.get_Item('COPY')===installed,'callback-installed object replaced');
  const foreign=new DxfXRecord();source.NamedObjects.Add('EXTERNAL',foreign);item.PersistentReactors.Add(foreign);const mapped=new DxfXRecord();target.NamedObjects.Add('TARGET',mapped);
  const lookup={LookupCalls:0,*[Symbol.iterator](){yield [foreign,mapped];},TryGetValue(){this.LookupCalls++;throw new IOException('Caller lookup must not run during clone mutation');},get(){this.LookupCalls++;throw new IOException('Caller lookup must not run during clone mutation');}};
  const clone=target.Objects.CloneObject(item,target.NamedObjects,'RETRY',lookup);Equal(0,lookup.LookupCalls,'caller lookup after snapshot');Check(clone.PersistentReactors.Contains(mapped),'snapshot external mapping');
}
function OutputSettingsValidation() {
  let plot=OutputPayload(25);plot.ScaleToFit=false;Equal(25,plot.StandardScaleType,'false retains explicit code');plot.ScaleToFit=true;Equal(0,plot.StandardScaleType,'true selects fit');plot.ScaleToFit=false;Equal(16,plot.StandardScaleType,'legacy false selects1:1');
  Throws(ArgumentOutOfRangeException,()=>{plot.StandardScaleType=33;});Throws(ArgumentOutOfRangeException,()=>{plot.StandardScaleFactor=NaN;});Equal(plot.StandardScaleFactor,plot.Clone().StandardScaleFactor,'legacy clone147');
  const doc=new DxfDocument(),count=doc.Objects.Items.Count,seed=doc.DrawingVariables.HandleSeed;plot.PaperSize=new Vector2(NaN,1);Throws(ArgumentException,()=>doc.Objects.AddPlotSettings('BAD',plot));Equal(count,doc.Objects.Items.Count,'invalid author registration');Equal(seed,doc.DrawingVariables.HandleSeed,'invalid author seed');plot=OutputPayload(1);Throws(ArgumentException,()=>doc.Objects.AddPlotSettings('bad\ud800',plot));Equal(count,doc.Objects.Items.Count,'invalid name registration');
  const page=doc.Objects.AddPlotSettings('PAGE',plot);plot.WindowBottomLeft=Vector2.Zero;Equal(new Vector2(-11.25,-22.5),page.Settings.WindowBottomLeft,'author copied input');const foreign=new DxfDocument(),object=new DxfXRecord();foreign.NamedObjects.Add('FOREIGN',object);page.Settings.ShadePlotObject=object;
  const out=new MemoryStream();try{saveRejected(doc,out);Equal(0,out.Length,'foreign shade wrote output');}finally{out.Dispose();}page.Settings.ShadePlotObject=null;
  const embedded=A(doc.Layouts)[0].PlotSettings;embedded.ShadePlotObject=new DxfXRecord();const second=new MemoryStream();try{saveRejected(doc,second);Equal(0,second.Length,'detached embedded shade output');}finally{second.Dispose();}embedded.ShadePlotObject=null;
  embedded.PlotterName='a\nb';const text=new MemoryStream();try{saveRejected(doc,text);Equal(0,text.Length,'embedded framing output');}finally{text.Dispose();}
}
function OutputSettingsMalformed(kind,scenario) {
  const doc=new DxfDocument(DxfVersion.AutoCad2018),page=doc.Objects.AddPlotSettings('PAGE',OutputPayload(25)),layout=doc.Layouts.Add(new Layout('SHEET'));layout.PlotSettings=OutputPayload(25);const wipe=doc.Objects.SetWipeoutVariables(false),handle=kind==='PLOTSETTINGS'?page.Handle:kind==='LAYOUT'?layout.Handle:wipe.Handle,valid=new MemoryStream();
  try{Check(doc.Save(valid,true),'malformed seed');valid.Position=0;let raw=DxfRawDocument.Load(valid);raw=replaceRecord(raw,handle,t=>{
    const start=t.findIndex(x=>x.Code===100&&x.Value===(kind==='WIPEOUTVARIABLES'?'AcDbWipeoutVariables':'AcDbPlotSettings')),find=code=>t.findIndex((x,i)=>i>start&&x.Code===code);
    if(kind==='LAYOUT'&&scenario>=9){const boundary=t.findIndex((x,i)=>i>start&&x.Code===100);if(scenario===9)t[boundary]=T(100,'PrivateUnsupportedLayout');else t.splice(boundary);return t;}
    if(kind==='WIPEOUTVARIABLES'){if(scenario===0)t[find(70)]=T(70,2);else t.splice(start+1,0,T(70,0));}
    else if(scenario===0)t[find(75)]=T(75,33);else if(scenario===1)t[find(143)]=T(143,0);else if(scenario===2)t[find(142)]=T(142,-1);else if(scenario===3)t[find(72)]=T(72,9);else if(scenario===4)t[find(73)]=T(73,9);else if(scenario===5)t[find(78)]=T(78,99);else if(scenario===6)t.splice(start+1,0,T(75,1));else if(scenario===7)t.splice(start+1,0,T(333,'DEADBEEF'));else t[find(2)]=T(2,String.raw`bad\U+000Atext`);return t;
  });for(const b of [false,true]){const out=new MemoryStream();try{raw.Save(out,b);out.Position=0;let rejected;try{rejected=DxfDocument.Load(out)===null;}catch(e){if(!(e instanceof FormatException))throw e;rejected=true;}Check(rejected,'Malformed output settings accepted');}finally{out.Dispose();}}}finally{valid.Dispose();}
}
function OutputSettingsStoredFactor(version,binary) {
  for(const factor of [0,-.5,.03125]) {
    const doc=new DxfDocument(version),plot=OutputPayload(0);plot.StandardScaleFactor=factor;const page=doc.Objects.AddPlotSettings('FACTOR',plot),sheet=doc.Layouts.Add(new Layout('FACTOR_SHEET'));sheet.PlotSettings=plot.Clone();
    const stream=new MemoryStream();try{Check(doc.Save(stream,binary),'stored factor save');stream.Position=0;const raw=DxfRawDocument.Load(stream),records=A(raw.Sections).flatMap(s=>A(s.Records));
      for(const h of [page.Handle,sheet.Handle]){const r=OleSingle(records.filter(r=>A(r.Tags).some(t=>t.Code===5&&t.Value===h)));SameDoubleBits(factor,OleSingle(A(r.Tags).filter(t=>t.Code===147)).Value,'raw147 bits changed');}
      stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'factor load');for(const s of [named(loaded).get_Item('FACTOR').Settings,loaded.Layouts.get_Item('FACTOR_SHEET').PlotSettings]){Equal(factor,s.StandardScaleFactor,'finite147 retention');Equal(0,s.StandardScaleType,'fit code changed');Equal(2.5,s.PrintScaleNumerator,'custom numerator changed');Equal(17.75,s.PrintScaleDenominator,'custom denominator changed');}
    }finally{stream.Dispose();}
  }
}
function OutputSettingsShadeRetry() {
  for(const b of [false,true])for(const embedded of [false,true])for(const foreign of [false,true]) {
    const doc=new DxfDocument(),page=doc.Objects.AddPlotSettings('PAGE',OutputPayload(25)),layout=doc.Layouts.Add(new Layout('SHEET'));layout.PlotSettings=OutputPayload(25);
    const initial=new MemoryStream();try{Check(doc.Save(initial,b),'retry baseline save');}finally{initial.Dispose();}
    const settings=embedded?layout.PlotSettings:page.Settings,bad=new DxfXRecord(),other=new DxfDocument();if(foreign)other.NamedObjects.Add('SHADE',bad);settings.ShadePlotObject=bad;const count=doc.Objects.Items.Count,seed=doc.DrawingVariables.HandleSeed,out=new MemoryStream();
    try{saveRejected(doc,out);Equal(0,out.Length,'invalid reference emitted bytes');Equal(count,doc.Objects.Items.Count,'invalid reference registered objects');Equal(seed,doc.DrawingVariables.HandleSeed,'invalid reference allocated handles');Check(settings.ShadePlotObject===bad,'failed save changed reference');}finally{out.Dispose();}
    settings.ShadePlotObject=null;const retry=new MemoryStream();try{Check(doc.Save(retry,b),'corrected reference retry save');retry.Position=0;Check(DxfDocument.Load(retry)!==null,'corrected reference retry load');}finally{retry.Dispose();}
  }
}
function OutputSettingsShadeProfiles() {
  for(const version of [DxfVersion.AutoCad2000,DxfVersion.AutoCad2004])for(const binary of [false,true])for(const embedded of [false,true]) {
    const doc=DxfDocument.Load(fixture(DxfVersion.AutoCad2018)),page=named(doc).get_Item('Independent-07').Settings,layout=doc.Layouts.get_Item('IndependentSheet').PlotSettings;
    if(embedded)page.ShadePlotObject=null;else layout.ShadePlotObject=null;doc.DrawingVariables.AcadVer=version;
    const seed=doc.DrawingVariables.HandleSeed,count=doc.Objects.Items.Count,out=new MemoryStream();try{saveRejected(doc,out);Equal(0,out.Length,'unqualified shade profile emitted bytes');Equal(seed,doc.DrawingVariables.HandleSeed,'unqualified shade allocated handles');Equal(count,doc.Objects.Items.Count,'unqualified shade registered objects');}finally{out.Dispose();}
    doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2007;const promoted=new MemoryStream();try{Check(doc.Save(promoted,binary),'promoted shade save');promoted.Position=0;let raw=DxfRawDocument.Load(promoted);
      raw=DxfRawDocument.Create(A(raw.Tags).map(t=>t.Code===1&&t.Value==='AC1021'?T(1,version===DxfVersion.AutoCad2000?'AC1015':'AC1018'):t));
      const older=new MemoryStream();try{raw.Save(older,binary);older.Position=0;const retained=DxfDocument.Load(older);Check(retained!==null,'older shade load');const settings=embedded?retained.Layouts.get_Item('IndependentSheet').PlotSettings:named(retained).get_Item('Independent-07').Settings;Check(settings.ShadePlotObject!==null,'older profile lost retained reference');
        const reject=new MemoryStream();try{saveRejected(retained,reject);Equal(0,reject.Length,'retained unqualified shade emitted bytes');}finally{reject.Dispose();}
        retained.DrawingVariables.AcadVer=DxfVersion.AutoCad2007;const retry=new MemoryStream();try{Check(retained.Save(retry,binary),'retained shade promotion retry');}finally{retry.Dispose();}
      }finally{older.Dispose();}
    }finally{promoted.Dispose();}
  }
}
function OutputSettingsGraphicalShade() {
  const doc=new DxfDocument(DxfVersion.AutoCad2018),block=new Block('ATTRIBUTES'),definition=new AttributeDefinition('TAG');definition.Value='sentinel';block.AttributeDefinitions.Add(definition);const insert=new Insert(block);doc.Entities.Add(insert);
  const page=doc.Objects.AddPlotSettings('PAGE',OutputPayload(25)),layout=doc.Layouts.Add(new Layout('SHEET'));layout.PlotSettings=OutputPayload(25);const valid=new DxfDictionaryVariable();doc.NamedObjects.Add('SHADE',valid);page.Settings.ShadePlotObject=valid;
  for(const graphical of [definition,OleSingle(insert.Attributes)]){
    Throws(ArgumentException,()=>{page.Settings.ShadePlotObject=graphical;});Check(page.Settings.ShadePlotObject===valid,'rejected graphical target changed setting');const count=doc.Objects.Items.Count,seed=doc.DrawingVariables.HandleSeed;
    Throws(ArgumentException,()=>doc.Objects.CloneObject(page,doc.NamedObjects,'BAD_CLONE',new Map([[valid,graphical]])));Equal(count,doc.Objects.Items.Count,'graphical clone registered objects');Equal(seed,doc.DrawingVariables.HandleSeed,'graphical clone allocated handles');
    for(const embedded of [false,true])for(const binary of [false,true]) {
      const stream=new MemoryStream();try{Check(doc.Save(stream,binary),'graphical source save');stream.Position=0;let raw=DxfRawDocument.Load(stream);
        raw=replaceRecord(raw,embedded?layout.Handle:page.Handle,t=>{const start=t.findIndex(x=>x.Code===100&&x.Value==='AcDbPlotSettings');let end=t.findIndex((x,i)=>i>start&&(x.Code===100||x.Code===1001));if(end<0)end=t.length;const ref=t.findIndex((x,i)=>i>start&&i<end&&x.Code===333);if(ref<0)t.splice(end,0,T(333,graphical.Handle));else t[ref]=T(333,graphical.Handle);return t;});
        const malformed=new MemoryStream();try{raw.Save(malformed,binary);malformed.Position=0;let rejected;try{rejected=DxfDocument.Load(malformed)===null;}catch(e){if(!(e instanceof FormatException))throw e;rejected=true;}Check(rejected,'graphical incoming333 accepted');}finally{malformed.Dispose();}
      }finally{stream.Dispose();}
    }
  }
}
function OutputSettingsFreshPreflight() {
  for(const b of [false,true])for(const v of [DxfVersion.AutoCad2000,DxfVersion.AutoCad2004,DxfVersion.AutoCad2018]) {
    const doc=new DxfDocument(v),settings=A(doc.Layouts)[0].PlotSettings;if(v<DxfVersion.AutoCad2007)settings.ShadePlotObject=doc.Layers.get_Item('0');else settings.PaperSize=new Vector2(NaN,10);const seed=doc.DrawingVariables.HandleSeed;
    Check(PeekDocumentObjects(doc)===null,'fresh fixture initialized database');const out=new MemoryStream();try{saveRejected(doc,out);Equal(0,out.Length,'invalid first save emitted bytes');Equal(seed,doc.DrawingVariables.HandleSeed,'invalid first save allocated handles');Check(PeekDocumentObjects(doc)===null,'invalid first save initialized database');}finally{out.Dispose();}
    settings.ShadePlotObject=null;settings.PaperSize=new Vector2(210,297);const retry=new MemoryStream();try{Check(doc.Save(retry,b),'corrected first-save retry');}finally{retry.Dispose();}
  }
}
function OutputSettingsCloneIdentity() {
  for(const kind of ['object','dictionary','extension']) {
    const source=new DxfDocument(),target=new DxfDocument(),third=new DxfDocument(),sourceStyle=source.TextStyles.Add(new TextStyle('SAME_NAME','txt.shx')),decoy=third.TextStyles.Add(new TextStyle('SAME_NAME','txt.shx'));
    Check(sourceStyle.Equals(decoy),'decoy must compare equal by name');const targetStyle=target.TextStyles.Add(new TextStyle('TARGET','txt.shx')),other=target.TextStyles.Add(new TextStyle('OTHER','txt.shx')),sourceOwner=new Line(Vector3.Zero,Vector3.UnitX);source.Entities.Add(sourceOwner);let targetOwner=new Line(Vector3.Zero,Vector3.UnitX);target.Entities.Add(targetOwner);
    const graph=new DxfDictionary(),buffer=new DxfIdBuffer();buffer.References.Add(sourceStyle);buffer.PersistentReactors.Add(sourceStyle);const data=new XData(new ApplicationRegistry('QA_IDENTITY'));data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,sourceStyle.Handle));buffer.XData.Add(data);graph.Add('BUFFER',buffer);
    if(kind==='extension')source.Objects.SetExtensionDictionary(sourceOwner,graph);else source.NamedObjects.Add('GRAPH',graph);
    const destination=target.NamedObjects,count=target.Objects.Items.Count,seed=target.DrawingVariables.HandleSeed;
    const copy=(map,name='COPY')=>kind==='object'?target.Objects.CloneObject(buffer,destination,name,map):kind==='dictionary'?target.Objects.Clone(graph,destination,name,map):target.Objects.CloneExtensionDictionary(sourceOwner,targetOwner,map);
    Throws(InvalidOperationException,()=>copy(new Map([[decoy,targetStyle]])));Equal(count,target.Objects.Items.Count,'decoy registered clones');Equal(seed,target.DrawingVariables.HandleSeed,'decoy allocated handles');Check(!destination.Contains('COPY')&&targetOwner.ExtensionDictionary===null,'decoy occupied destination');
    const copied=copy(new Map([[sourceStyle,targetStyle],[decoy,other]])),result=copied instanceof DxfIdBuffer?copied:copied.get_Item('BUFFER');Check(result.References.get_Item(0)===targetStyle&&result.PersistentReactors.Contains(targetStyle),'exact mapping source identity');Equal(targetStyle.Handle,result.XData.get_Item('QA_IDENTITY').XDataRecord.get_Item(0).Value,'exact XData mapping');Check(buffer.References.get_Item(0)===sourceStyle,'clone changed source identity');
    if(kind==='extension'){targetOwner=new Line(Vector3.Zero,Vector3.UnitY);target.Entities.Add(targetOwner);}
    const mapping={*[Symbol.iterator](){yield [sourceStyle,targetStyle];sourceStyle.Name='RENAMED';},get(){throw new InvalidOperationException('Caller lookup must not be used');},ContainsKey(){throw new InvalidOperationException('Caller lookup must not be used');},TryGetValue(){throw new InvalidOperationException('Caller lookup must not be used');}};
    const renamed=copy(mapping,'RENAMED_COPY'),renamedBuffer=renamed instanceof DxfIdBuffer?renamed:renamed.get_Item('BUFFER');Check(renamedBuffer.References.get_Item(0)===targetStyle,'Renaming source key broke identity mapping');
  }
  const source=new DxfDocument(),target=new DxfDocument(),third=new DxfDocument(),sourceOwner=source.TextStyles.Add(new TextStyle('OWNER','txt.shx')),decoy=third.TextStyles.Add(new TextStyle('OWNER','txt.shx')),targetOwner=target.TextStyles.Add(new TextStyle('OWNER','txt.shx')),unrelated=target.TextStyles.Add(new TextStyle('UNRELATED','txt.shx')),extension=new DxfDictionary(),buffer=new DxfIdBuffer();buffer.References.Add(sourceOwner);extension.Add('OWNER',buffer);source.Objects.SetExtensionDictionary(sourceOwner,extension);
  const cloned=target.Objects.CloneExtensionDictionary(sourceOwner,targetOwner,new Map([[decoy,unrelated]]));Check(cloned.get_Item('OWNER').References.get_Item(0)===targetOwner,'equal-name decoy replaced automatic owner mapping');
}
