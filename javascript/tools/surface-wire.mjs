import { UniqueKnot,BasisFunctionInput,BasisFunction,ParametricSurface,BSplineSurface,NURBSSurface } from '../index.js';
export function surfaceWire(v,wire){
 if(v instanceof UniqueKnot)return {type:'UniqueKnot',t:wire(v.T),multiplicity:v.Multiplicity};
 if(v instanceof BasisFunctionInput)return {type:'BasisFunctionInput',count:v.NumControls,degree:v.Degree,uniform:v.Uniform,periodic:v.Periodic,uniqueCount:v.NumUniqueKnots,unique:wire(v.UniqueKnots)};
 if(v instanceof BasisFunction)return {type:'BasisFunction',count:v.NumControls,degree:v.Degree,uniqueCount:v.NumUniqueKnots,knotCount:v.NumKnots,min:wire(v.MinDomain),max:wire(v.MaxDomain),open:v.IsOpen,uniform:v.IsUniform,periodic:v.IsPeriodic,unique:wire(v.UniqueKnots),knots:wire(v.Knots)};
 if(v instanceof ParametricSurface){const common={type:v.constructor.name,constructed:v.IsConstructed,umin:wire(v.UMin),umax:wire(v.UMax),vmin:wire(v.VMin),vmax:wire(v.VMax),rectangular:v.IsIsRectangular,counts:[v.NumControls(0),v.NumControls(1)],basis:[wire(v.BasisFunction(0)),wire(v.BasisFunction(1))]};
   if(v instanceof BSplineSurface)return {common,controls:wire(v.Controls())};
   if(v instanceof NURBSSurface)return {common,controls:wire(v.Controls),weights:wire(v.Weights)};
 }
 return undefined;
}
