#!/usr/bin/env python3
"""Verify stored LAYER_INDEX output graphs against raw counts and independent producers."""
from pathlib import Path
import argparse, copy, gzip, hashlib, json, struct, tempfile
import ezdxf
from verify_mleader_inputs import records, exact, decode_once, check
VERSIONS={2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
NAMES=['Alpha','Alpha','alpha','東京',r'Literal\U+0041']

def normal(tags):return [[c,decode_once(v) if isinstance(v,str) else v] for c,v in tags]
def body(tags,marker):return tags[tags.index([100,marker])+1:]
def one(tags,code):
    values=[v for c,v in tags if c==code];check(len(values)==1,f'Expected one group {code}, got {len(values)}');return values[0]
def owner(tags):
    prefix=tags[:next(i for i,t in enumerate(tags) if t[0]==100)];inside=False;values=[]
    for c,v in prefix:
        if c==102:inside=v!='}'
        elif c==330 and not inside:values.append(v)
    check(len(values)==1,'Expected one common owner');return values[0]
def dictionary(wire,handle):
    tags=normal(body(wire[handle],'AcDbDictionary'));result={};pending=None
    for c,v in tags:
        if c==3:check(pending is None and v not in result,'Duplicate/incomplete dictionary key');pending=v
        elif c in (350,360):check(pending is not None,'Dictionary handle lacks key');result[pending]=v;pending=None
    check(pending is None,'Unfinished dictionary key');return result

def open_output(path,year,binary):
    check(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport mismatch')
    doc=ezdxf.readfile(path);check(doc.dxfversion==VERSIONS[year],'Profile mismatch');return doc,records(path)
def classes(doc,count,year):
    cls=doc.classes.get('LAYER_INDEX');check(cls.dxf.cpp_class_name=='AcDbLayerIndex' and cls.dxf.is_an_entity==0,'Wrong LAYER_INDEX CLASS identity')
    check(cls.dxf.flags==0 and cls.dxf.was_a_proxy==0,'Wrong LAYER_INDEX CLASS flags')
    if year>=2004:check(cls.dxf.instance_count==count,'Wrong LAYER_INDEX CLASS count')
def audit(doc):
    result=doc.audit();check(not result.errors and not result.fixes,f'Independent audit: {len(result.errors)} errors/{len(result.fixes)} repairs')
def lines(doc):
    found={tuple(e.dxf.start):e.dxf.handle for e in doc.modelspace().query('LINE')};check(set(found)=={(1.,2.,3.),(-1.,-2.,-3.)},'External LINE inventory/geometry changed')
    a,b=found[(1.,2.,3.)],found[(-1.,-2.,-3.)]
    check(tuple(doc.entitydb[a].dxf.end)==(4.,5.,6.) and tuple(doc.entitydb[b].dxf.end)==(-4.,-5.,-6.),'External LINE endpoints changed');return a,b

def index_packet(wire,handle,parent,names,counts,timestamp):
    tags=wire[handle];check(tags[0]==[0,'LAYER_INDEX'] and owner(tags)==parent,'Index identity/owner mismatch')
    check([v for c,v in tags if c==100]==['AcDbIndex','AcDbLayerIndex'],'Public index subclasses changed')
    check(struct.pack('<d',one(tags,40))==struct.pack('<d',timestamp),'Timestamp bits changed')
    packet=normal(body(tags,'AcDbLayerIndex'));check([c for c,v in packet]==[8,360,90]*len(names),'Expected complete interleaved name/handle/count tuples')
    check([v for c,v in packet if c==8]==names and [v for c,v in packet if c==90]==counts,'Stored names/counts changed')
    handles=[v for c,v in packet if c==360];check(len(set(handles))==len(handles) and all(h!='0' for h in handles),'Repeated/null ownership buffer')
    for handle,count in zip(handles,counts):
        b=wire[handle];check(b[0]==[0,'IDBUFFER'] and owner(b)==one(tags,5),'Buffer type/reciprocal owner mismatch')
        values=body(b,'AcDbIdBuffer');refs=[v for c,v in values if c==330];check(len(refs)==count,'Count differs from actual buffer list')
        check(all(v=='0' or v in wire for v in refs),'Unresolved buffer soft reference')
    return handles

def graph(path,year,binary,copy_graph=False,wire_override=None):
    doc,wire=open_output(path,year,binary)
    if wire_override is not None:wire=wire_override
    root=dictionary(wire,doc.rootdict.dxf.handle);key='COPY' if copy_graph else 'QA_LAYER_INDEX';parent=root[key];edges=dictionary(wire,parent)
    check(set(edges)=={'INDEX','INDEX_ALIAS','EMPTY'} and edges['INDEX_ALIAS']==edges['INDEX'],'Index alias or dictionary inventory changed')
    index=edges['INDEX'];empty=edges['EMPTY'];a,b=lines(doc)
    buffers=index_packet(wire,index,parent,NAMES,[4,0,1,1,1],2451545.125)
    index_packet(wire,empty,parent,[],[],-17.125)
    expected=[[a,b,a,'0'],[],[b],[index],[buffers[0]]]
    for h,refs in zip(buffers,expected):check([v for c,v in body(wire[h],'AcDbIdBuffer') if c==330]==refs,'Exact buffer reference sequence changed')
    data=body(wire[buffers[1]],'AcDbIdBuffer');check([t for t in data if t[0]>=1000]==[[1001,'INDEX_APP'],[1004,{'hex':'0300ff'}],[1005,index]],'Binary or handle XData changed')
    tags=wire[index];start=tags.index([102,'{ACAD_REACTORS']);end=next(i for i in range(start+1,len(tags)) if tags[i]==[102,'}']);check(tags[start+1:end]==[[330,parent],[330,a]],'Index persistent reactors changed')
    start=tags.index([102,'{ACAD_XDICTIONARY']);end=next(i for i in range(start+1,len(tags)) if tags[i]==[102,'}']);check(end==start+2 and tags[start+1][0]==360,'Index extension envelope malformed')
    extension=tags[start+1][1];check(owner(wire[extension])==index,'Extension dictionary owner changed');note=dictionary(wire,extension)['NOTE'];check(owner(wire[note])==extension,'Extension XRecord owner changed')
    check([t for t in body(wire[note],'AcDbXrecord') if t[0]!=280]==[[1,'index metadata'],[330,buffers[0]]],'Extension payload/reference changed')
    check(sum(t[0]==[0,'LAYER_INDEX'] for t in wire.values())==2 and sum(t[0]==[0,'IDBUFFER'] for t in wire.values())==5,'Application object inventory changed')
    classes(doc,2,year);audit(doc)
    return {'index':index,'empty':empty,'parent':parent,'buffers':buffers,'extension':extension,'note':note,'line_a':a,'line_b':b},wire

def erased(path,year,binary,previous):
    doc,wire=open_output(path,year,binary);check(not any(t[0] in ([0,'LAYER_INDEX'],[0,'IDBUFFER']) for t in wire.values()),'Erased application objects returned')
    check('COPY' not in dictionary(wire,doc.rootdict.dxf.handle),'Erased root alias returned')
    for h in [previous[k] for k in ('index','empty','parent','extension','note')]+previous['buffers']:check(h not in wire,'Erased graph identity survived')
    a,b=lines(doc);check((a,b)==(previous['line_a'],previous['line_b']),'Erasure changed external LINE identities');classes(doc,0,year);audit(doc)

def producer_sources(root):
    folder=root/'tests/fixtures/layer-index';manifest=json.loads((folder/'manifest.json').read_text());check(len(manifest['fixtures'])==12,'Expected 12 pinned producer originals')
    sources={}
    with tempfile.TemporaryDirectory() as temporary:
        for fixture in manifest['fixtures']:
            packed=(folder/fixture['file']).read_bytes();check(hashlib.sha256(packed).hexdigest()==fixture['gzip_sha256'],'Pinned gzip hash mismatch');data=gzip.decompress(packed);check(hashlib.sha256(data).hexdigest()==fixture['sha256'],'Original source hash mismatch')
            original=Path(temporary)/'source.dxf';original.write_bytes(data);old=records(original);path=folder/fixture['extracted_file'];check(hashlib.sha256(path.read_bytes()).hexdigest()==fixture['extracted_sha256'],'Extracted source hash mismatch');new=records(path);mapping=fixture['handle_map'];check(len(fixture['application_packets'])==6,'Expected six original application packets')
            for h in fixture['application_packets']:
                expected=[[c,mapping.get(v,v) if c in (5,330,360) else v] for c,v in old[h]];check(exact(new[mapping[h]])==exact(expected),'Source application packet changed beyond declared handle map')
            audit(ezdxf.readfile(path));year=int(fixture['file'].split('-R')[1].split('-')[0]);sources[(year,'-binary.' in fixture['file'])]=(path,new)
    return sources

def producer(path,year,source_binary,binary,sources):
    source,old=sources[(year,source_binary)];before=ezdxf.readfile(source);doc,wire=open_output(path,year,binary);parent=dictionary(wire,doc.rootdict.dxf.handle)['QA_LAYER_INDEX'];edges=dictionary(wire,parent);index=edges['INDEX'];empty=edges['EMPTY'];a,b=lines(doc)
    buffers=index_packet(wire,index,parent,['Alpha','Alpha','alpha','Missing'],[3,0,1,1],2451545.625);index_packet(wire,empty,parent,[],[],2451545.5)
    for h,refs in zip(buffers,[[a,b,a],[],[a],[a]]):check([v for c,v in body(wire[h],'AcDbIdBuffer') if c==330]==refs,'Producer buffer sequence changed')
    original_parent=before.rootdict['QA_LAYER_INDEX'].dxf.handle;check(parent==original_parent and index==before.rootdict['QA_LAYER_INDEX']['INDEX'].dxf.handle,'Extracted source identity changed')
    for h in [index,empty]+buffers:
        check(owner(wire[h])==owner(old[h]),'Producer ownership changed')
        if wire[h][0]==[0,'IDBUFFER']:check(exact(body(wire[h],'AcDbIdBuffer'))==exact(body(old[h],'AcDbIdBuffer')),'Producer buffer payload changed')
    classes(doc,2,year);audit(doc)

def opaque(path,variant,binary,baseline,wire_override=None):
    doc,wire=open_output(path,2018,binary)
    if wire_override is not None:wire=wire_override
    parent=dictionary(wire,doc.rootdict.dxf.handle)['QA_LAYER_INDEX'];index=dictionary(wire,parent)['INDEX'];expected=copy.deepcopy(baseline[index]);first=next(i for i,t in enumerate(expected) if t[0]==100);marker=expected.index([100,'AcDbLayerIndex'])
    if variant==0:expected.insert(first,[1,'private header'])
    elif variant==1:expected[first:first]=[[102,'{PRIVATE'],[70,17],[102,'}']]
    elif variant==2:expected.extend([[100,'PrivateLayerIndex'],[91,27]])
    elif variant==3:expected.insert(marker+1,[90,0])
    elif variant==4:expected[first]=[100,'PrivateIndexBase']
    else:expected.append([91,7])
    check(exact(normal(wire[index]))==exact(normal(expected)),'Whole opaque index packet changed')
    check(sum(t[0]==[0,'IDBUFFER'] for t in wire.values())==5,'Opaque graph lost owned buffers')
    for h,t in baseline.items():
        if t[0]==[0,'IDBUFFER']:check(exact(normal(wire[h]))==exact(normal(t)),'Opaque graph altered buffer packet')
    classes(doc,2,2018);audit(doc);return index,wire

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);parser.add_argument('--repository',type=Path,default=Path(__file__).resolve().parents[1]);args=parser.parse_args();root=args.repository
    expected={f'layer-index-{kind}-AutoCad{year}-{binary}.dxf' for kind in ('authored','copy','erased') for year in VERSIONS for binary in (False,True)}
    expected|={f'layer-index-producer-AutoCad{year}-{source}-{binary}.dxf' for year in VERSIONS for source in (False,True) for binary in (False,True)}
    expected|={f'layer-index-opaque-{variant}-{binary}.dxf' for variant in range(6) for binary in (False,True)}
    check({p.name for p in args.directory.glob('layer-index-*.dxf')}==expected,'Expected all 72 LAYER_INDEX output fixtures')
    sources=producer_sources(root);baselines={}
    for year in VERSIONS:
        for binary in (False,True):
            authored=args.directory/f'layer-index-authored-AutoCad{year}-{binary}.dxf';original,wire=graph(authored,year,binary)
            if year==2018:baselines[binary]=wire
            copied,copy_wire=graph(args.directory/f'layer-index-copy-AutoCad{year}-{binary}.dxf',year,binary,True)
            check(all(original[k]!=copied[k] for k in ('index','empty','parent','extension','note')) and all(a!=b for a,b in zip(original['buffers'],copied['buffers'])),'Clone reused source graph handles')
            erased(args.directory/f'layer-index-erased-AutoCad{year}-{binary}.dxf',year,binary,copied)
            for source in (False,True):producer(args.directory/f'layer-index-producer-AutoCad{year}-{source}-{binary}.dxf',year,source,binary,sources)
    for binary in (False,True):
        for variant in range(6):opaque(args.directory/f'layer-index-opaque-{variant}-{binary}.dxf',variant,binary,baselines[binary])
    checks=0
    path=args.directory/'layer-index-authored-AutoCad2018-False.dxf';identities,wire=graph(path,2018,False)
    for mode in ('bad-count','bad-owner','literal-escape'):
        bad=copy.deepcopy(wire);index=identities['index']
        if mode=='bad-count':at=next(i for i,t in enumerate(bad[index]) if t[0]==90);bad[index][at]=[90,3]
        elif mode=='bad-owner':handle=identities['buffers'][0];at=next(i for i,t in enumerate(bad[handle]) if t[0]==330);bad[handle][at]=[330,identities['parent']]
        else:at=next(i for i,t in enumerate(bad[index]) if t[0]==8 and 'Literal' in t[1]);bad[index][at]=[8,r'Literal\U+0041']
        try:graph(path,2018,False,wire_override=bad)
        except (ValueError,KeyError):checks+=1
    check(checks==3,'Output corruption controls were not detected')
    print(f'PASS ezdxf {ezdxf.__version__}: 72 outputs, 12 pinned originals/extractions, 72 exact mapped source packets, counts/ownership/clone/erase/opaque graphs, zero output audit repairs and 3 rejected corruptions')
if __name__=='__main__':main()
