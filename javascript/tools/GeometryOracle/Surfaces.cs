// Serialization only: expectations come from the unchanged pinned production assembly.
using System;
using netDxf.GTE;
internal static partial class Program {
 private static bool SurfaceWire(object value,out object? result){
  result=null;
  if(value is UniqueKnot k){result=new {type="UniqueKnot",t=Wire(k.T),multiplicity=k.Multiplicity};return true;}
  if(value is BasisFunctionInput i){result=new {type="BasisFunctionInput",count=i.NumControls,degree=i.Degree,uniform=i.Uniform,periodic=i.Periodic,uniqueCount=i.NumUniqueKnots,unique=Wire(i.UniqueKnots)};return true;}
  if(value is BasisFunction b){result=new {type="BasisFunction",count=b.NumControls,degree=b.Degree,uniqueCount=b.NumUniqueKnots,knotCount=b.NumKnots,min=Wire(b.MinDomain),max=Wire(b.MaxDomain),open=b.IsOpen,uniform=b.IsUniform,periodic=b.IsPeriodic,unique=Wire(b.UniqueKnots),knots=Wire(b.Knots)};return true;}
  if(value is ParametricSurface s){
   int Count(int dim)=>s is BSplineSurface bs?bs.NumControls(dim):((NURBSSurface)s).NumControls(dim);
   object? Basis(int dim)=>Wire(s is BSplineSurface bs?bs.BasisFunction(dim):((NURBSSurface)s).BasisFunction(dim));
   var common=new {type=s.GetType().Name,constructed=s.IsConstructed,umin=Wire(s.UMin),umax=Wire(s.UMax),vmin=Wire(s.VMin),vmax=Wire(s.VMax),rectangular=s.IsIsRectangular,counts=new[]{Count(0),Count(1)},basis=new[]{Basis(0),Basis(1)}};
   if(s is BSplineSurface bs)result=new {common,controls=Wire(bs.Controls())};
   else if(s is NURBSSurface ns)result=new {common,controls=Wire(ns.Controls),weights=Wire(ns.Weights)};
   else return false;return true;
  }return false;
 }
}
