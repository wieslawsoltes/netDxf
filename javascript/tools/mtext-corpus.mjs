// Additional independently evaluated MTEXT inputs. No expected results are stored here.
import { D,R,E,A,V,I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',signature)=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const Q=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const snap=target=>({kind:'snapshot',target});
const e=(type,value)=>E('Entities.'+type,value);
const matrix=values=>({new:'Matrix3',args:values.map(D)});
const matrices=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[1,0,0,0,-1,0,0,0,1],
  [2,0,0,0,2,0,0,0,2],[2,0,0,0,3,0,0,0,4],[0,-1,0,1,0,0,0,0,1],[0,0,0,0,0,0,0,0,0],[1,.25,0,0,1,0,0,0,1]];
const text=()=>N('MText',['ABCD',V('Vector3',1,2,3),D(2.5),D(10)]);
const columns=(mode=0,storage=2)=>[N('MTextColumns',[],'c'),S('c','Type',e('MTextColumnType',mode===0?1:2)),S('c','Storage',e('MTextColumnStorage',storage)),
  S('c','Count',I(2)),S('c','AutoHeight',mode===1),S('c','Width',D(12.5)),S('c','Gutter',D(1.75)),S('c','DefinedHeight',D(mode===2?0:20.25)),S('c','TotalHeight',D(25.5)),
  ...(mode===2?[G('c','Heights','hs'),C('hs','Add',[D(20.25)]),C('hs','Add',[D(0)])]:[])];
export function mtextCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name,category,request:{steps}});
  const texts=[null,'','Hello','Zażółć 日本😀','a\0b','{group}','\\','prefix\\','a\\{b\\}c\\\\d','\\Lunder\\l\\Oover\\o\\Kstrike\\k',
    'one\\Ptwo\\Xthree','\\~hard space','\\FArial|b1;A','\\Q37;B','\\S1#2;','\\S1/2;','\\SA^B;','\\SA^ B;','\\S^ B;','\\SA^ ;','before\\Sbad','before\\Sbad\\','before\\Sbad^',
    '\\S1\\;2;tail','\\qunknown;Z','x{a}{b}y','\\c123;\\C7;Color','\\U+0041','😀\\P\ud800'];
  for(let i=0;i<texts.length;i++)add(`mtext/plain/${i}`,'mtext-text',[N('MText',[texts[i] === null ? null : {utf16:texts[i].split('').map(c=>c.charCodeAt(0))}]),C('p','PlainText'),C('p','Clone',[],'q'),G('q','Value','value')]);
  const pos2=V('Vector2',2,3),pos3=V('Vector3',2,3,4),style={new:'Tables.TextStyle',args:['ST','font.shx']};
  for(const args of [[],['text'],[pos2,D(2)],[pos3,D(2)],[pos2,D(2),D(4)],[pos3,D(2),D(4)],
    [pos2,D(2),D(4),style],[pos3,D(2),D(4),style],['A',pos2,D(2)],['A',pos3,D(2)],['A',pos2,D(2),D(4)],['A',pos3,D(2),D(4)],
    ['A',pos2,D(2),D(4),style],['A',pos3,D(2),D(4),style]])add(`mtext/construct/${out.length}`,'mtext-model',[N('MText',args),C('p','Clone')]);
  for(const value of [-1,0,NaN,Infinity])add(`mtext/construct-height/${value}`,'mtext-guards',[N('MText',['A',pos3,D(value)])]);
  add('mtext/construct-style-null','mtext-guards',[N('MText',['A',pos3,D(1),D(1),null], 'p',['String','Vector3','Double','Double','Tables.TextStyle'])]);
  for(const width of [-1,-0,0,NaN,Infinity])add(`mtext/construct-width/${width}`,'mtext-model',[N('MText',['A',pos3,D(1),D(width)]),C('p','Clone')]);
  for(const field of ['Height','RectangleWidth','LineSpacingFactor','DefinedHeight'])for(const value of [-Infinity,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,.249,.25,1,4,4.01,Infinity,NaN])
    add(`mtext/${field}/${Object.is(value,-0)?'-0':value}`,'mtext-guards',[text(),S('p',field,D(value)),snap('p'),C('p','Clone')]);
  for(const value of [-1,0,1,2,3,4,32767])add(`mtext/spacing-style/${value}`,'mtext-guards',[text(),S('p','LineSpacingStyle',e('MTextLineSpacingStyle',value)),snap('p')]);
  for(let flags=0;flags<128;flags++)add(`mtext/write/${flags}`,'mtext-format',[
    N('MText',[flags%2?'start':null]),N('MTextFormattingOptions',[],'o'),
    ...['Bold','Italic','Overline','Underline','StrikeThrough','Superscript','Subscript'].map((k,i)=>S('o',k,(flags&(1<<i))!==0)),
    S('o','HeightFactor',D(1.5)),S('o','SuperSubScriptHeightFactor',D(.625)),S('o','ObliqueAngle',D(-12)),S('o','CharacterSpaceFactor',D(1.25)),S('o','WidthFactor',D(.75)),
    S('o','FontName',flags%3===0?'Custom Font':null),S('o','Color',{static:'AciColor',property:flags%2?'Red':'ByLayer'}),C('p','Write',['abc{\\}',R('o')]),C('p','PlainText'),snap('p')]);
  add('mtext/write-truecolor','mtext-format',[N('MText'),N('MTextFormattingOptions',[],'o'),{kind:'call',type:'AciColor',member:'FromTrueColor',args:[I(0x123456)],signature:['Int32'],id:'color'},
    S('o','Color',R('color')),C('p','Write',['abc',R('o')]),C('p','PlainText'),C('p','Write',[null]),snap('p')]);
  for(const fraction of [-1,0,1,2,3])for(const options of [false,true])add(`mtext/fraction/${fraction}/${options}`,'mtext-format',[
    N('MText'),N('MTextFormattingOptions',[],'o'),S('o','Underline',true),C('p','WriteFraction',[null,'den',E('Units.FractionFormatType',fraction),options?R('o'):null]),C('p','PlainText'),snap('p')]);
  for(let alignment=0;alignment<7;alignment++)for(let spacing=0;spacing<5;spacing++)add(`mtext/paragraph/${alignment}/${spacing}`,'mtext-format',[
    N('MText'),N('MTextParagraphOptions',[],'o'),S('o','Alignment',e('MTextParagraphAlignment',alignment)),S('o','LineSpacingStyle',e('MTextLineSpacingStyle',spacing)),
    S('o','VerticalAlignment',e('MTextParagraphVerticalAlignment',alignment%4)),S('o','HeightFactor',D(1.5)),S('o','LeftIndent',D(2)),S('o','FirstLineIndent',D(-3)),
    S('o','RightIndent',D(.75)),S('o','SpacingBefore',D(.25)),S('o','SpacingAfter',D(.5)),S('o','LineSpacingFactor',D(1.25)),C('p','StartParagraph',[R('o')]),C('p','Write',['abc']),C('p','EndParagraph'),C('p','PlainText'),snap('p')]);
  add('mtext/paragraph-default','mtext-format',[N('MText',[null]),C('p','StartParagraph'),C('p','EndParagraph'),C('p','PlainText'),snap('p')]);
  for(const mirror of [false,true])for(let attachment=1;attachment<=9;attachment++)for(let i=0;i<matrices.length;i++)
    add(`mtext/transform/${mirror}/${attachment}/${i}`,'mtext-transforms',[text(),{kind:'set',type:'Entities.MText',member:'DefaultMirrText',value:mirror},S('p','AttachmentPoint',e('MTextAttachmentPoint',attachment)),
      S('p','Rotation',D(37)),C('p','TransformBy',[matrix(matrices[i]),V('Vector3',3,4,5)],null,['Matrix3','Vector3']),snap('p'),C('p','Clone')]);
  for(const field of ['Width','Gutter','DefinedHeight','TotalHeight','StoredTotalWidth','EmbeddedReferenceWidth'])for(const value of [-1,-0,0,1,NaN,Infinity])
    add(`columns/${field}/${Object.is(value,-0)?'-0':value}`,'column-validation',[N('MTextColumns'),S('p',field,D(value)),C('p','Validate'),snap('p'),C('p','Clone')]);
  for(const field of ['Type','Storage','Count'])for(const value of [-1,0,1,2,3,32767,32768])add(`columns/${field}/${value}`,'column-validation',[
    N('MTextColumns'),S('p',field,field==='Count'?I(value):e(field==='Type'?'MTextColumnType':'MTextColumnStorage',value)),C('p','Validate'),snap('p')]);
  for(let mode=0;mode<3;mode++)for(let storage=0;storage<3;storage++)add(`columns/valid/${mode}/${storage}`,'column-model',[
    ...columns(mode,storage),...(storage===1?[N('MText',['linked'],'linked'),G('c','LinkedColumns','links'),C('links','Add',[R('linked')])]:[]),C('c','Validate'),C('c','Clone',[],'copy'),snap('copy')]);
  for(let mode=0;mode<3;mode++)for(const reverse of [false,true])add(`columns/conversion/${mode}/${reverse}`,'column-conversion',[
    text(),...columns(mode),S('c','FlowReversed',reverse),S('c','EmbeddedInsertionPoint',V('Vector3',9,8,7)),S('c','EmbeddedTextDirection',V('Vector3',0,1,0)),S('c','EmbeddedReferenceWidth',D(99)),
    S('p','Columns',R('c')),S('p','Rotation',D(23)),C('p','ConvertToLinkedColumns',[A('String',['AB','CD'])],'all'),Q('all',0,'main'),Q('all',1,'second'),
    C('main','ConvertToEmbeddedColumns',[],'merged'),snap('merged'),snap('p'),C('main','Clone',[],'copy'),S('second','Value','changed'),snap('copy'),snap('main')]);
  for(const parts of [null,[],['A'],['A',null],['A','X']])add(`columns/partition/${JSON.stringify(parts)}`,'column-validation',[
    text(),...columns(),S('p','Columns',R('c')),C('p','ConvertToLinkedColumns',[parts===null?null:A('String',parts)])]);
  for(let mode=0;mode<3;mode++)for(let i=0;i<matrices.length;i++)add(`columns/transform/${mode}/${i}`,'column-transform',[
    text(),...columns(mode),S('p','Columns',R('c')),S('c','EmbeddedInsertionPoint',V('Vector3',9,8,7)),S('p','DefinedHeight',D(20)),
    C('p','TransformBy',[matrix(matrices[i]),V('Vector3',3,4,5)],null,['Matrix3','Vector3']),snap('p'),C('p','Clone')]);
  for(const field of ['EmbeddedTextDirection','EmbeddedInsertionPoint'])for(const v of [[0,0,0],[0,0,1],[NaN,1,0],[Infinity,0,1]])add(`columns/vector/${field}/${v}`,'column-validation',[
    N('MTextColumns'),S('p',field,V('Vector3',...v)),C('p','Validate'),C('p','Clone'),G('p',field,'v'),S('v','X',D(42)),snap('p')]);
  for(const mode of ['null','duplicate','nested','self'])add(`columns/graph/${mode}`,'column-validation',[
    ...columns(0,1),S('c','Count',I(mode==='duplicate'?3:2)),N('MText',['L'],'link'),G('c','LinkedColumns','links'),
    ...(mode==='nested'||mode==='self'?[S('link','Columns',mode==='self'?R('c'):{new:'Entities.MTextColumns',args:[]})]:[]),
    C('links','Add',[mode==='null'?null:R('link')]),...(mode==='duplicate'?[C('links','Add',[R('link')])]:[]),C('c','Validate'),C('c','Clone'),snap('c')]);
  for(const field of ['Height','DrawingDirection','Rotation'])add(`columns/format-mismatch/${field}`,'column-conversion',[
    text(),...columns(),S('p','Columns',R('c')),C('p','ConvertToLinkedColumns',[A('String',['AB','CD'])],'all'),Q('all',0,'main'),Q('all',1,'second'),
    S('second',field,field==='DrawingDirection'?e('MTextDrawingDirection',1):D(123)),C('main','ConvertToEmbeddedColumns'),snap('main')]);
  add('mtext/background-clone','mtext-model',[text(),N('MTextBackgroundFill',[],'bg'),S('bg','TrueColor',I(-1)),S('bg','Transparency',I(-2147483648)),S('p','BackgroundFill',R('bg')),
    C('p','Clone',[],'q'),G('q','BackgroundFill','qbg'),S('qbg','TrueColor',I(42)),snap('p'),snap('q')]);
  return out;
}
