// Runs only inside the offline-installed package; no checkout or oracle imports.
{
  const api=await import('@netdxf/javascript'),g=api.Gte;
  const standalone=await import('@netdxf/javascript/netDxf/GTE/GMatrix.js');
  if(standalone.GMatrix!==g.GMatrix||api.BezierCurve===g.BezierCurve)throw new Error('GTE namespace identities differ.');
  for(const rowMajor of [true,false]) {
    g.GTE.UseRowMajor=rowMajor;const m=new g.GMatrix(2,2,[1,2,3,4]);
    m.set_Item(0,1,7);if(m.get_Item(0,1)!==7||m.get_Item(rowMajor?1:2)!==7)throw new Error('GTE overloaded indexers differ.');
    m.MakeIdentity();if(m.get_Item(0,0)!==1||m.get_Item(1,1)!==1||m.get_Item(1,0)!==0)throw new Error('GTE matrix identity differs.');
  }
  g.GTE.UseRowMajor=true;
  const curve=new g.BezierCurve([new api.Vector3(0,0,0),new api.Vector3(1,0,0),new api.Vector3(2,0,0),new api.Vector3(3,0,0)],3),jet={value:null};
  curve.Evaluate(.5,1,jet);if(jet.value[0].X!==1.5||jet.value[1].X!==3)throw new Error('GTE Bezier jet differs.');
  const roots={value:null};g.RootsPolynomial.SolveQuadratic(-1,0,1,roots);
  if(roots.value.Count!==2||roots.value.get_Item(-1)!==1||roots.value.get_Item(1)!==1)throw new Error('GTE quadratic roots differ.');
  const {GteSortedDictionary}=await import('@netdxf/javascript/runtime/GteRuntime.js'),map=new GteSortedDictionary();map.Add(0,1);
  try{map.Add(-0,2);throw new Error('Duplicate key accepted');}catch(error){if(error.name!=='ArgumentException'||error.ParamName!==null)throw error;}
}
