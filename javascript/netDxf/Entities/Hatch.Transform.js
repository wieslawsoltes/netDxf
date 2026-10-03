// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {HatchBoundaryPath as H} from './HatchBoundaryPath.js';
import {HatchGradientPattern} from './HatchGradientPattern.js';
import {HatchFillType} from './HatchFillType.js';
import {HatchSplineData} from './HatchSplineData.js';
import {Vector2} from '../Vector2.js';
import {Vector3} from '../Vector3.js';
import {Matrix3} from '../Matrix3.js';
import {MathHelper} from '../MathHelper.js';
import {FixedArray} from '../../runtime/FixedArray.js';
import {DotNetMath as M,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {RequireAffineFinite as finite,AffineUnit as unit,AffineCross as cross} from '../../runtime/HatchAffine.js';
import {TransformAffineArc,TransformAffineConic,TransformAffinePolyline} from './Hatch.ConicTransform.js';
import {HasExplicitAffinePattern,TransformAffinePattern} from './Hatch.PatternTransform.js';
import {ArgumentException,NotSupportedException} from '../../runtime/Errors.js';
function validate(paths){
  for(const path of paths)for(const edge of path.Edges){
    if(edge instanceof H.Line){finite(edge.Start);finite(edge.End);}
    else if(edge instanceof H.Spline){HatchSplineData.Validate(edge);for(const p of edge.FitPoints)finite(p);if(edge.StartTangent!==null)finite(edge.StartTangent);if(edge.EndTangent!==null)finite(edge.EndTangent);}
    else if(edge instanceof H.Polyline){if(edge.Vertexes==null)throw new ArgumentException('HATCH polyline vertices must be supplied.');for(const p of edge.Vertexes)finite(p);}
    else if(edge instanceof H.Arc){finite(edge.Center);finite(edge.Radius);finite(edge.StartAngle);finite(edge.EndAngle);}
    else if(edge instanceof H.Ellipse){finite(edge.Center);finite(edge.EndMajorAxis);finite(edge.MinorRatio);finite(edge.StartAngle);finite(edge.EndAngle);}
    else throw new ArgumentException('Unsupported or absent HATCH edge.');
  }
}
function splineCopy(spline,map){
  HatchSplineData.Validate(spline);
  const copy=Object.assign(new H.Spline(),{Degree:spline.Degree,IsRational:spline.IsRational,IsPeriodic:spline.IsPeriodic,Knots:FixedArray(spline.Knots,v=>v),ControlPoints:FixedArray(spline.ControlPoints),StartTangent:spline.StartTangent,EndTangent:spline.EndTangent});
  for(const p of spline.FitPoints)copy.FitPoints.Add(p);
  for(let i=0;i<copy.ControlPoints.length;i++){const v=copy.ControlPoints[i],p=map(new Vector2(v.X,v.Y),false);copy.ControlPoints[i]=new Vector3(p.X,p.Y,v.Z);}
  for(let i=0;i<copy.FitPoints.Count;i++)copy.FitPoints.set_Item(i,map(copy.FitPoints.get_Item(i),false));
  if(copy.StartTangent!==null)copy.StartTangent=map(copy.StartTangent,true);if(copy.EndTangent!==null)copy.EndTangent=map(copy.EndTangent,true);return copy;
}
/** Stage the complete edge/pattern/seed geometry before touching the source model. */
export function TransformHatch(h,transformation,translation){
  for(let r=1;r<=3;r++)for(let c=1;c<=3;c++)finite(transformation[`M${r}${c}`]);
  finite(translation);finite(h.Elevation);finite(h.Pattern.Angle);finite(h.Pattern.Scale);validate(h.BoundaryPaths);
  const oldOcs=MathHelper.ArbitraryAxis(h.Normal),xAxis=Matrix3.Multiply(transformation,Matrix3.Multiply(oldOcs,Vector3.UnitX)),yAxis=Matrix3.Multiply(transformation,Matrix3.Multiply(oldOcs,Vector3.UnitY));
  finite(xAxis);finite(yAxis);const xUnit=unit(xAxis),yUnit=unit(yAxis);let normal=unit(Vector3.CrossProduct(xUnit,yUnit));
  const hint=Matrix3.Multiply(transformation,h.Normal);finite(hint);if(Vector3.DotProduct(normal,hint)<0)normal=Vector3.Negate(normal);
  const newOcs=MathHelper.ArbitraryAxis(normal).Transpose(),xLength=xAxis.Modulus(),yLength=yAxis.Modulus();finite(xLength);finite(yLength);
  const similarity=M.Abs(Vector3.DotProduct(xUnit,yUnit))<=1e-12&&M.Abs(xLength-yLength)<=mul(1e-12,M.Max(xLength,yLength)),explicit=HasExplicitAffinePattern(h);
  if(!similarity&&(h.Pattern instanceof HatchGradientPattern||(h.Pattern.Fill!==HatchFillType.SolidFill&&(!explicit||h.Pattern.IsDouble))))throw new NotSupportedException('Nonuniform transforms require solid fill or explicit nondoubled patterns.');
  const position=Matrix3.Multiply(newOcs,Vector3.Add(Matrix3.Multiply(transformation,Matrix3.Multiply(oldOcs,new Vector3(0,0,h.Elevation))),translation));finite(position);
  const map=(value,vector)=>{
    finite(value.X);finite(value.Y);const point=Matrix3.Multiply(newOcs,Vector3.Add(Matrix3.Multiply(transformation,Matrix3.Multiply(oldOcs,new Vector3(value.X,value.Y,vector?0:h.Elevation))),vector?Vector3.Zero:translation));finite(point);return new Vector2(point.X,point.Y);
  };
  const paths=[];
  for(const path of h.BoundaryPaths){
    const edges=[];
    for(const edge of path.Edges){
      if(edge instanceof H.Line)edges.push(Object.assign(new H.Line(),{Start:map(edge.Start,false),End:map(edge.End,false)}));
      else if(edge instanceof H.Spline)edges.push(splineCopy(edge,map));
      else if(edge instanceof H.Arc)edges.push(TransformAffineArc(edge,map,similarity));
      else if(edge instanceof H.Ellipse)edges.push(TransformAffineConic(edge.Center,edge.EndMajorAxis,edge.MinorRatio,edge.StartAngle,edge.EndAngle,edge.IsCounterclockwise,map));
      else TransformAffinePolyline(edge,map,similarity,cross(map(Vector2.UnitX,true),map(Vector2.UnitY,true))<0,edges);
    }
    const output=H.FromEdges(edges);output.PathType=(path.PathType&~2)|(output.PathType&2);paths.push(output);
  }
  validate(paths);const seeds=Array.from(h.SeedPoints,seed=>map(seed,false));
  const direction=Vector2.Multiply(h.Pattern.Scale,Vector2.Rotate(Vector2.UnitX,mul(h.Pattern.Angle,MathHelper.DegToRad))),axis=map(direction,true),scale=axis.Modulus(),angle=mul(Vector2.Angle(axis),MathHelper.RadToDeg);
  finite(scale);finite(angle);if(scale<=0)throw new ArgumentException('The pattern direction collapses.');
  let identity=translation.X===0&&translation.Y===0&&translation.Z===0;for(let r=1;r<=3;r++)for(let c=1;c<=3;c++)identity=identity&&transformation[`M${r}${c}`]===(r===c?1:0);
  const transformedPattern=explicit?TransformAffinePattern(h,transformation,translation,map,scale,angle,identity):null;
  if(h.Associative)h.UnLinkBoundary();if(identity)return;
  if(transformedPattern!==null)h.Pattern=transformedPattern;else{h.Pattern.Scale=scale;h.Pattern.Angle=angle;}
  h.Elevation=position.Z;h.Normal=normal;h.BoundaryPaths.Clear();h.BoundaryPaths.AddRange(paths);h.SeedPoints.Clear();for(const seed of seeds)h.SeedPoints.Add(seed);
}
