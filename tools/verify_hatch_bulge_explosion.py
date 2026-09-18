#!/usr/bin/env python3
"""Verify exploded bulge orientation in physical HATCH packets and an independent reader."""
import io
import math
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader,binary_tags_loader
PROFILES=dict(zip(('AutoCad2000','AutoCad2004','AutoCad2007','AutoCad2010','AutoCad2013','AutoCad2018'),('AC1015','AC1018','AC1021','AC1024','AC1027','AC1032')))
BULGES=(-2.,-1.,-.25,.25,1.,2.)
def require(test,message):
    if not test:raise ValueError(message)
def inputs(index,turn):
    a=(2.,-3.);b=(8.,5.)
    if turn:a=(-a[1],a[0]);b=(-b[1],b[0])
    bulge=BULGES[index];dx,dy=b[0]-a[0],b[1]-a[1];scale=(1/bulge-bulge)/4
    c=((a[0]+b[0])/2-dy*scale,(a[1]+b[1])/2+dx*scale)
    radius=math.hypot(dx,dy)*(1+bulge*bulge)/(4*abs(bulge))
    return a,b,bulge,c,radius
def boundary(data):
    load=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    packets=[];hatch=False;active=False;current=[]
    for tag in load:
        if tag.code==0:hatch=tag.value=='HATCH'
        if not hatch:continue
        if tag.code==91:active=True
        if active and tag.code==75:packets.append(current);current=[];active=False
        if active:current.append((tag.code,float(tag.value)))
    require(len(packets)==1,'Expected one HATCH boundary');return packets[0]
def expected(index,turn):
    a,b,bulge,c,r=inputs(index,turn);sign=1 if bulge>0 else -1
    angle=lambda p:(sign*math.degrees(math.atan2(p[1]-c[1],p[0]-c[0])))%360
    return [(91,1),(92,5),(93,2),(72,2),(10,c[0]),(20,c[1]),(40,r),(50,angle(a)),(51,angle(b)),(73,int(bulge>0)),
            (72,1),(10,b[0]),(20,b[1]),(11,a[0]),(21,a[1]),(97,0)]
def check(tags,index,turn):
    wanted=expected(index,turn);require(len(tags)==len(wanted),'Boundary inventory differs')
    for at,((code,value),(ec,ev)) in enumerate(zip(tags,wanted)):
        require(code==ec,'Boundary framing/order differs')
        if at in (4,5,6,7,8):require(math.isfinite(value) and abs(value-ev)<=1e-10*max(1,abs(ev)),'Arc geometry differs')
        else:require(value==ev,'Boundary value/orientation differs')
def curve_point(a,c,r,bulge,t):
    angle=math.atan2(a[1]-c[1],a[0]-c[0])+4*math.atan(bulge)*t
    return c[0]+r*math.cos(angle),c[1]+r*math.sin(angle)
def near(a,b,bound):require(math.hypot(a[0]-b[0],a[1]-b[1])<=bound,'Independent edge geometry/traversal differs')
def main(directory):
    directory=Path(directory);names={f'hatch-bulge-explosion-{i}-{t}-{v}-{b}.dxf' for i in range(6) for t in (0,1) for v in PROFILES for b in (False,True)}
    require({p.name for p in directory.glob('hatch-bulge-explosion-*.dxf')}==names,'Fixture inventory differs');mutations=0;sample_count=0
    for i in range(6):
        for turn in (0,1):
            a,b,bulge,c,r=inputs(i,turn)
            for version,profile in PROFILES.items():
                for binary in (False,True):
                    path=directory/f'hatch-bulge-explosion-{i}-{turn}-{version}-{binary}.dxf';data=path.read_bytes();require(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport differs')
                    tags=boundary(data);check(tags,i,turn)
                    for at,(code,value) in enumerate(tags):
                        for op in ('change','drop','duplicate'):
                            changed=list(tags)
                            if op=='change':changed[at]=(code,value+1)
                            elif op=='drop':del changed[at]
                            else:changed.insert(at,changed[at])
                            try:check(changed,i,turn)
                            except ValueError:mutations+=1
                            else:raise AssertionError('Corrupted boundary escaped positive validator')
                    doc=ezdxf.readfile(path);require(doc.dxfversion==profile,'Profile differs');hatches=doc.modelspace().query('HATCH');require(len(hatches)==1,'HATCH count')
                    paths=hatches[0].paths;require(len(paths)==1 and len(paths[0].edges)==2,'Reader boundary shape')
                    arc,line=paths[0].edges;require(arc.ccw==(bulge>0),'Reader orientation flag');bound=1e-10*max(1,r,*map(abs,c))
                    near(arc.real_start_point,a,bound);near(arc.real_end_point,b,bound);near(line.start,b,0);near(line.end,a,0)
                    # ezdxf stores both orientations internally as CCW geometry;
                    # its real-start/real-end properties preserve path traversal.
                    sweep=(arc.end_angle-arc.start_angle)%360
                    for n in range(33):
                        t=n/32;angle=math.radians(arc.start_angle+sweep*(t if arc.ccw else 1-t))
                        actual=(arc.center.x+arc.radius*math.cos(angle),arc.center.y+arc.radius*math.sin(angle))
                        near(actual,curve_point(a,c,r,bulge,t),bound);sample_count+=1
                    audit=doc.audit();require(not audit.errors and not audit.fixes,'Graph repairs required')
    print(f'PASS: {len(names)} bulge HATCH drawings / {sample_count} independently loaded traversal samples; {mutations} packet corruptions rejected; zero graph errors/repairs')
if __name__=='__main__':main(sys.argv[1])
