// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {HatchBoundaryPath as H} from './HatchBoundaryPath.js';
import {Vector2 as V} from '../Vector2.js';
import {Vector3} from '../Vector3.js';
import {MathHelper} from '../MathHelper.js';
import {FixedArray} from '../../runtime/FixedArray.js';
import {DotNetMath as M,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {RequireAffineFinite as finite,AffineHypot as hypot,AffineCross as cross,AffineAngle as normalize,AffinePolarDirection as polar} from '../../runtime/HatchAffine.js';
import {ArgumentException,NotSupportedException} from '../../runtime/Errors.js';
function interval(originalStart,originalEnd,start,end){
  const span=originalEnd-originalStart;finite(span);
  if(M.Abs(span)>360)throw new NotSupportedException('HATCH conic transforms support a single stored turn.');
  if(span===0)end=start;else if(M.Abs(span)===360){end=start+360;start=end-360;}else if(start===end)throw new ArgumentException('The conic interval cannot be represented.');
  return [start,end];
}
export function AffineConicPoint(center,major,ratio,angle,ccw){
  const p=polar(ccw?angle:-angle);let parameter=new V(mul(ratio,p.X),p.Y);parameter=V.Divide(parameter,hypot(parameter.X,parameter.Y));
  return V.Add(V.Add(center,V.Multiply(major,parameter.X)),V.Multiply(new V(-major.Y,major.X),mul(ratio,parameter.Y)));
}
export function ValidateAffineConicEndpoints(ellipse,start,end){
  finite(start);finite(end);const scale=M.Max(1,M.Max(M.Max(M.Abs(start.X),M.Abs(start.Y)),M.Max(M.Abs(end.X),M.Abs(end.Y))));
  for(const first of [true,false]){
    const expected=first?start:end,actual=AffineConicPoint(ellipse.Center,ellipse.EndMajorAxis,ellipse.MinorRatio,first?ellipse.StartAngle:ellipse.EndAngle,ellipse.IsCounterclockwise);finite(actual);
    if(M.Abs(actual.X-expected.X)>mul(1e-10,scale)||M.Abs(actual.Y-expected.Y)>mul(1e-10,scale))throw new ArgumentException('Transformed HATCH conic endpoints are not representable within source tolerance.');
  }
}
export function TransformAffineArc(arc,map,similarity){
  if(arc.Radius<=0)throw new ArgumentException('A transformed arc requires a positive radius.');
  if(!similarity)return TransformAffineConic(arc.Center,new V(arc.Radius,0),1,arc.StartAngle,arc.EndAngle,arc.IsCounterclockwise,map);
  const x=map(new V(arc.Radius,0),true),y=map(new V(0,arc.Radius),true),radius=hypot(x.X,x.Y);finite(radius);
  if(radius===0)throw new ArgumentException('The transformed arc collapses.');
  const ccw=arc.IsCounterclockwise===(cross(V.Divide(x,radius),V.Divide(y,radius))>0);
  const angle=value=>{const direction=polar(arc.IsCounterclockwise?value:-value),point=V.Add(V.Multiply(x,direction.X),V.Multiply(y,direction.Y)),a=mul(M.Atan2(point.Y,point.X),MathHelper.RadToDeg);return normalize(ccw?a:-a);};
  const [start,end]=interval(arc.StartAngle,arc.EndAngle,angle(arc.StartAngle),angle(arc.EndAngle));
  const result=Object.assign(new H.Arc(),{Center:map(arc.Center,false),Radius:radius,StartAngle:start,EndAngle:end,IsCounterclockwise:ccw});
  ValidateAffineConicEndpoints(Object.assign(new H.Ellipse(),{Center:result.Center,EndMajorAxis:new V(radius,0),MinorRatio:1,StartAngle:start,EndAngle:end,IsCounterclockwise:ccw}),map(AffineConicPoint(arc.Center,new V(arc.Radius,0),1,arc.StartAngle,arc.IsCounterclockwise),false),map(AffineConicPoint(arc.Center,new V(arc.Radius,0),1,arc.EndAngle,arc.IsCounterclockwise),false));
  return result;
}
export function TransformAffineConic(center,majorAxis,ratio,originalStart,originalEnd,originalCcw,map){
  if(ratio<=0||(majorAxis.X===0&&majorAxis.Y===0))throw new ArgumentException('An ellipse requires nonzero axes and a positive ratio.');
  const u=map(majorAxis,true),v=map(new V(mul(-majorAxis.Y,ratio),mul(majorAxis.X,ratio)),true);
  const largest=M.Max(M.Max(M.Abs(u.X),M.Abs(u.Y)),M.Max(M.Abs(v.X),M.Abs(v.Y)));finite(largest);
  if(largest===0)throw new ArgumentException('The transformed conic collapses.');
  const a=V.Divide(u,largest),b=V.Divide(v,largest),xx=mul(a.X,a.X)+mul(b.X,b.X),xy=mul(a.X,a.Y)+mul(b.X,b.Y),yy=mul(a.Y,a.Y)+mul(b.Y,b.Y);
  const eigenvalue=mul(.5,xx+yy+hypot(xx-yy,mul(2,xy))),major=M.Sqrt(eigenvalue),determinant=cross(a,b),minor=M.Abs(determinant)/major;
  if(minor===0)throw new ArgumentException('The conic minor axis is not representable.');
  let direction;
  if(xy===0)direction=xx===yy?V.Divide(a,hypot(a.X,a.Y)):xx>yy?V.UnitX:V.UnitY;
  else {const first=new V(eigenvalue-yy,xy),second=new V(xy,eigenvalue-xx);direction=hypot(first.X,first.Y)>=hypot(second.X,second.Y)?first:second;direction=V.Divide(direction,hypot(direction.X,direction.Y));}
  if(V.DotProduct(direction,a)<0)direction=V.Negate(direction);
  const perpendicular=new V(-direction.Y,direction.X),outputRatio=minor/major,length=mul(major,largest);finite(outputRatio);finite(length);
  if(outputRatio<=0||length<=0)throw new ArgumentException('The transformed ellipse is not representable.');
  const ccw=originalCcw===(determinant>0);
  const angle=value=>{const p=polar(originalCcw?value:-value);let parameter=new V(mul(ratio,p.X),p.Y);parameter=V.Divide(parameter,hypot(parameter.X,parameter.Y));const point=V.Add(V.Multiply(a,parameter.X),V.Multiply(b,parameter.Y)),r=mul(M.Atan2(V.DotProduct(point,perpendicular),V.DotProduct(point,direction)),MathHelper.RadToDeg);return normalize(ccw?r:-r);};
  const [start,end]=interval(originalStart,originalEnd,angle(originalStart),angle(originalEnd));
  const result=Object.assign(new H.Ellipse(),{Center:map(center,false),EndMajorAxis:V.Multiply(direction,length),MinorRatio:M.Min(1,outputRatio),StartAngle:start,EndAngle:end,IsCounterclockwise:ccw});
  ValidateAffineConicEndpoints(result,map(AffineConicPoint(center,majorAxis,ratio,originalStart,originalCcw),false),map(AffineConicPoint(center,majorAxis,ratio,originalEnd,originalCcw),false));return result;
}
export function TransformAffinePolyline(polyline,map,similarity,reverses,output){
  const vs=polyline.Vertexes,segments=polyline.IsClosed?vs.length:M.Max(0,vs.length-1);let curved=false;for(let i=0;i<segments;i++)curved||=vs[i].Z!==0;
  if(similarity||!curved){
    output.push(Object.assign(new H.Polyline(),{IsClosed:polyline.IsClosed,Vertexes:FixedArray(Array.from(vs,(v,i)=>{const p=map(new V(v.X,v.Y),false),bulge=i<segments&&v.Z!==0&&reverses?-v.Z:v.Z;return new Vector3(p.X,p.Y,bulge);} ))}));return;
  }
  if(!polyline.IsClosed&&vs[vs.length-1].Z!==0)throw new NotSupportedException('Converting the open polyline would discard its unused terminal bulge.');
  for(let i=0;i<segments;i++){
    const first=vs[i],second=vs[(i+1)%vs.length],start=new V(first.X,first.Y),end=new V(second.X,second.Y),bulge=first.Z;
    if(bulge===0){output.push(Object.assign(new H.Line(),{Start:map(start,false),End:map(end,false)}));continue;}
    const chord=V.Subtract(end,start),length=hypot(chord.X,chord.Y);if(length===0)throw new ArgumentException('Nonzero bulges require distinct endpoints.');
    const left=new V(-chord.Y/length,chord.X/length),distance=mul(length/4,1/bulge-bulge),radius=mul(length/4,M.Abs(bulge)+1/M.Abs(bulge));finite(distance);finite(radius);
    const center=V.Add(V.Add(start,V.Divide(chord,2)),V.Multiply(left,distance));finite(center);
    const sa=mul(M.Atan2(start.Y-center.Y,start.X-center.X),MathHelper.RadToDeg),ea=mul(M.Atan2(end.Y-center.Y,end.X-center.X),MathHelper.RadToDeg),ccw=bulge>0;
    const result=TransformAffineConic(center,new V(radius,0),1,normalize(ccw?sa:-sa),normalize(ccw?ea:-ea),ccw,map);ValidateAffineConicEndpoints(result,map(start,false),map(end,false));output.push(result);
  }
}
