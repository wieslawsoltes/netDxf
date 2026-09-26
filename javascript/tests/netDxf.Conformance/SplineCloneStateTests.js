// Complete detached isolation cases; typed document and block cases remain unregistered.
import {Spline,Vector3,Layer,AciColor,Lineweight,Transparency,XData,XDataCode,XDataRecord,ApplicationRegistry,SplineKnotParameterization} from '../../index.js';
import {Run,Check,Equal,SameDoubleBits} from './TestHarness.js';
import {AssertSplineTangent} from './SplineTangentTransformTests.js';
const same=(a,b)=>a.length===b.length&&Array.from(a).every((v,i)=>v?.Equals?v.Equals(b[i]):Object.is(v,b[i]));
export function RegisterSplineCloneStateTests(){for(let kind=0;kind<3;kind++)Run(`spline/clone-state/isolation/${kind}`,()=>SplineCloneIsolation(kind));}
export function CloneStateSpline(kind){
 const points=[new Vector3(1,2,3),new Vector3(4,7,8),new Vector3(9,5,2),new Vector3(12,3,7)];const s=kind===0?new Spline(points):new Spline(points,[1,.5,.75,1],2,kind===2);
 s.ControlPoints[1]=Vector3.Add(s.ControlPoints[1],new Vector3(.125,-.25,.5));s.Weights[1]=.875;for(let i=0;i<s.Knots.length;i++)s.Knots[i]=s.Knots[i]*3+2;
 Object.assign(s,{KnotParameterization:SplineKnotParameterization.FitCustom,KnotTolerance:.00125,CtrlPointTolerance:.0025,FitTolerance:.00375,StartTangent:new Vector3(2,-3,5),EndTangent:Vector3.Zero,Normal:new Vector3(1,2,3),IsVisible:false});
 s.Layer=new Layer('SplineCloneLayer');s.Layer.Color=new AciColor(4);s.Color=new AciColor(12,34,56);s.Lineweight=Lineweight.W25;s.LinetypeScale=2.25;s.Transparency=new Transparency(25);
 const data=new XData(new ApplicationRegistry('SPLINE_CLONE_STATE'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'retain geometry; do not refit'));s.XData.Add(data);return s;
}
export function SameSplineCloneState(expected,actual,normal=true){
 for(const key of ['ControlPoints','FitPoints','Knots','Weights'])Check(same(expected[key],actual[key]),'Clone changed '+key);
 for(const key of ['CreationMethod','IsClosedPeriodic','Degree','KnotParameterization','IsVisible','StartTangent','EndTangent'])Equal(expected[key],actual[key],key);
 for(const key of ['KnotTolerance','CtrlPointTolerance','FitTolerance'])SameDoubleBits(expected[key],actual[key],key);
 if(normal)AssertSplineTangent(expected.Normal,actual.Normal,'Normal');Equal(expected.Layer.Name,actual.Layer.Name,'Layer');Equal(AciColor.ToTrueColor(expected.Color),AciColor.ToTrueColor(actual.Color),'Color');
 Equal(expected.LinetypeScale,actual.LinetypeScale,'Linetype scale');Equal(expected.Lineweight,actual.Lineweight,'Lineweight');Equal(expected.Transparency.Value,actual.Transparency.Value,'Transparency');
 Equal('retain geometry; do not refit',actual.XData.get_Item('SPLINE_CLONE_STATE').XDataRecord.get_Item(0).Value,'XData');
}
export function SplineCloneIsolation(kind){
 const original=CloneStateSpline(kind),copy=original.Clone();SameSplineCloneState(original,copy);
 Check(['ControlPoints','Knots','Weights','FitPoints'].every(k=>original[k]!==copy[k]),'Clone shares spline arrays.');
 const control=original.ControlPoints[0],knot=original.Knots[0],weight=original.Weights[0];copy.ControlPoints[0]=new Vector3(-10,-20,-30);copy.Knots[0]=-15;copy.Weights[0]=.25;
 copy.Layer.Color.Index=1;copy.Color.Index=2;copy.Transparency.Value=50;copy.XData.get_Item('SPLINE_CLONE_STATE').XDataRecord.Clear();
 Equal(control,original.ControlPoints[0],'Shared control memory');Equal(knot,original.Knots[0],'Shared knot memory');Equal(weight,original.Weights[0],'Shared weight memory');Equal(4,original.Layer.Color.Index,'Shared layer');Equal(25,original.Transparency.Value,'Shared transparency');Equal(1,original.XData.get_Item('SPLINE_CLONE_STATE').XDataRecord.Count,'Shared XData');
 if(original.FitPoints.Count!==0){const point=original.FitPoints[0];copy.FitPoints[0]=Vector3.Zero;Equal(point,original.FitPoints[0],'Shared fit memory');}
}
