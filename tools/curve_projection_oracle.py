"""Independent selected-record oracle for detached curve conversion fixtures.

Geometry is reconstructed from explicit source coordinates and Decimal OCS
frames, not from either DXF library's coordinate transform. Packet ordering and
all selected metadata are exact; XY allows 8 ULP of the largest source coordinate
and normal components allow 8 ULP. Handle values are excluded, framing is not.
"""
from decimal import Decimal, localcontext
import io
import math
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = {'AutoCad2000':'AC1015','AutoCad2004':'AC1018','AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024','AutoCad2013':'AC1027','AutoCad2018':'AC1032'}
NORMALS = [(0.,0.,1.),(0.,0.,-1.),(1.,0.,0.),(0.,1.,0.),(1.,2.,3.),(1e-13,-2e-13,1.)]
CONTROLS = [(2.,3.,5.),(-4.,6.,7.),(8.,-2.,9.),(3.,5.,-1.),(1.,7.,4.)]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def rejected(check, value):
    try:
        check(value)
    except ValueError:
        return 1
    raise AssertionError('Corruption escaped the positive validator')


def frame(index):
    with localcontext() as context:
        context.prec = 150
        def unit(v):
            length = sum(x*x for x in v).sqrt()
            return tuple(x/length for x in v)
        def cross(a,b):
            return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
        n = unit(tuple(Decimal.from_float(x) for x in NORMALS[index]))
        axis = (Decimal(0),Decimal(1),Decimal(0)) if abs(n[0]) < Decimal(1)/64 and abs(n[1]) < Decimal(1)/64 else (Decimal(0),Decimal(0),Decimal(1))
        x = unit(cross(axis,n)); y = unit(cross(n,x))
        return x,y,n


def projected(points, index):
    x,y,n = frame(index)
    with localcontext() as context:
        context.prec = 150
        return [(float(sum(a*Decimal.from_float(v) for a,v in zip(x,p))),
                 float(sum(a*Decimal.from_float(v) for a,v in zip(y,p)))) for p in points], tuple(map(float,n))


def packet(data, kind='LWPOLYLINE'):
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
    records=[]; current=[]
    for tag in tags:
        if tag.code == 0:
            if current and current[0] == (0,kind): records.append(current)
            current=[]
        value = tag.value if tag.code == 1004 else cast_tag_value(tag.code,tag.value)
        if tag.code == 1004 and isinstance(value,str): value = bytes.fromhex(value)
        current.append((tag.code,value))
    if current and current[0] == (0,kind): records.append(current)
    require(len(records)==1,'Selected entity inventory differs')
    p=records[0]
    require([c for c,_ in p[:3]]==[0,5,330],'Identity framing')
    require(sum(c==5 for c,_ in p)==1 and sum(c==330 for c,_ in p)==1,'Repeated identity')
    require(int(p[1][1],16)>0 and int(p[2][1],16)>0,'Invalid identity')
    return p[:1]+p[3:]


def common(version, kind):
    result=[(0,kind),(100,'AcDbEntity'),(67,0),(8,'PROJECTION')]
    if version=='AutoCad2000':
        result += [(62,5),(440,33554687)]
    else:
        result += [(62,137),(420,0x204060),(440,33554597)]
    result += [(6,'Dashed'),(370,35),(48,2.5),(60,1)]
    if version!='AutoCad2000': result.append((430,'Book$Ink'))
    if version not in ('AutoCad2000','AutoCad2004'): result.append((284,3))
    return result


def lw_expected(points, index, closed, elevation, version, continuous):
    xy,n=projected(points,index)
    result=common(version,'LWPOLYLINE')+[(100,'AcDbPolyline'),(90,len(points)),(70,int(closed)+(128 if continuous else 0)),(38,elevation),(39,0.)]
    for x,y in xy: result += [(10,x),(20,y),(42,0.)]
    return result + list(zip((210,220,230),n)) + [(1001,'CURVE_PROJECTION'),(1000,'retained'),(1004,b'\x09\x02\x06')]


def check_packet(actual, expected, scale=9.):
    require(len(actual)==len(expected),'Selected packet length differs')
    for (code,value),(wanted_code,wanted) in zip(actual,expected):
        require(code==wanted_code,'Selected tag order differs')
        if code in (10,20,30,210,220,230):
            require(math.isfinite(value),'Nonfinite coordinate')
            tolerance=8*math.ulp(scale if code in (10,20,30) else wanted)
            require(abs(value-wanted)<=tolerance,'Coordinate exceeds numerical contract')
        else:
            require(value==wanted,'Selected metadata differs')


def corrupt(value):
    if isinstance(value,bytes): return value+b'\x7f'
    if isinstance(value,str): return value+'_corrupted'
    if isinstance(value,float): return value+1.25
    return value+1


def corruptions(actual, check):
    count=0
    for i,(code,value) in enumerate(actual):
        changed=list(actual); changed[i]=(code,corrupt(value)); count+=rejected(check,changed)
        count+=rejected(check,actual[:i]+actual[i+1:])
        count+=rejected(check,actual[:i]+[actual[i]]+actual[i:])
        if code in (10,20,30,210,220,230):
            changed=list(actual); changed[i]=(code,math.nan);count+=rejected(check,changed)
    return count


def audit(path, version, binary):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Wrong transport')
    doc=ezdxf.readfile(path)
    require(doc.dxfversion==PROFILES[version] and len(doc.modelspace())==1,'Wrong version/modelspace')
    result=doc.audit(); require(not result.errors and not result.fixes,'DXF graph requires repair')
