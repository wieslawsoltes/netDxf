// Input-only numerical scenarios. No native result or precomputed root is embedded.
const i=int=>({int}), r=ref=>({ref}), out=name=>({out:name}), cell=name=>({cell:name});
const arr=(array,values)=>({array,values}), vec=values=>({new:'GTE.GVector',args:[arr('Double',values)]});
const v=(x,y,z)=>({new:'Vector3',args:[x,y,z]});
const create=(id,type,args=[],signature)=>({kind:'new',id,value:{new:type,args,...(signature?{signature}:{})}});
const get=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const call=(target,member,args=[],signature,id)=>({kind:'call',target,member,args,...(signature?{signature}:{}),...(id?{id}:{})});
const invoke=(type,member,args=[],signature,id)=>({kind:'call',type,member,args,...(signature?{signature}:{}),...(id?{id}:{})});
const snapshot=target=>({kind:'snapshot',target});
const setIndex=(target,args,value)=>({kind:'set-index',target,args,value});
const fn=(coefficients,throwAt)=>({function:{coefficients,...(throwAt?{throwAt}:{})}});
export function gteCorpus(){
  const all=[];const add=(name,category,steps,rowMajor=true)=>all.push({name:'gte/'+name,category,request:{op:'gte',rowMajor,steps}});
  let seed=0x713b15;const random=()=>((seed=(Math.imul(seed,1664525)+1013904223)>>>0)/4294967296);
  for(const n of [1,2,3,4,7])for(let sample=0;sample<5;sample++){
    const a=Array.from({length:n},()=>Math.floor(random()*16)-8),b=Array.from({length:n},()=>Math.floor(random()*16)-8);
    const steps=[create('a','GTE.GVector',[arr('Double',a)]),create('b','GTE.GVector',[arr('Double',b)]),get('a','Size'),get('a','Vector'),
      invoke('GTE.GVector','Dot',[r('a'),r('b')]),...['op_Addition','op_Subtraction'].map(m=>invoke('GTE.GVector',m,[r('a'),r('b')])),
      invoke('GTE.GVector','op_Multiply',[r('a'),-2],['GTE.GVector','Double']),invoke('GTE.GVector','op_Multiply',[0.5,r('b')],['Double','GTE.GVector']),
      invoke('GTE.GVector','op_Division',[r('a'),2]),invoke('GTE.GVector','HLift',[r('a'),1]),invoke('GTE.GVector','HProject',[r('a')]),
      ...[false,true].flatMap(robust=>[invoke('GTE.GVector','Length',[r('a'),robust]),invoke('GTE.GVector','Normalize',[cell('b'),robust],['GTE.GVector&','Boolean']),snapshot('b')]),
      call('a','MakeUnit',[i(n-1)]),snapshot('a'),call('a','MakeZero'),snapshot('a')];
    add(`vectors/${n}/${sample}`,'vectors',steps);
  }
  for(const n of [2,3,4])for(const robust of [false,true])add(`orthonormal/${n}/${robust}`,'vectors',[
    {kind:'value',id:'vectors',value:arr('GTE.GVector',Array.from({length:n},(_,j)=>vec(Array.from({length:n},(_,k)=>k===j?2:k<j?1:0))))},
    invoke('GTE.GVector','Orthonormalize',[i(n),cell('vectors'),robust],['Int32','GTE.GVector[]&','Boolean']),snapshot('vectors'),
    invoke('GTE.GVector','ComputeExtremes',[i(n),r('vectors'),out('min'),out('max')],['Int32','GTE.GVector[]','GTE.GVector&','GTE.GVector&'])]);
  for(const rowMajor of [true,false])for(const n of [1,2,3,4])for(let sample=0;sample<4;sample++){
    const a=Array.from({length:n*n},(_,j)=>j%(n+1)===0?4+j:Math.floor(random()*5)-2),b=Array.from({length:n*n},()=>Math.floor(random()*9)-4);
    const vector=Array.from({length:n},(_,j)=>j+1);
    add(`matrices/${rowMajor}/${n}/${sample}`,'matrices',[create('a','GTE.GMatrix',[i(n),i(n),arr('Double',a)]),create('b','GTE.GMatrix',[i(n),i(n),arr('Double',b)]),create('v','GTE.GVector',[arr('Double',vector)]),
      call('a','GetSize',[out('rows'),out('cols')],['Int32&','Int32&']),get('a','NumElements'),get('a','Elements'),
      {kind:'index',target:'a',args:[i(n-1),i(0)]},{kind:'index',target:'a',args:[i(n*n-1)]},
      ...['op_Addition','op_Subtraction','MultiplyAB','MultiplyABT','MultiplyATB','MultiplyATBT'].map(m=>invoke('GTE.GMatrix',m,[r('a'),r('b')])),
      invoke('GTE.GMatrix','op_Multiply',[r('a'),r('v')],['GTE.GMatrix','GTE.GVector']),invoke('GTE.GMatrix','op_Multiply',[r('v'),r('a')],['GTE.GVector','GTE.GMatrix']),
      ...['L1Norm','L2Norm','LInfinityNorm','Transpose','Determinant'].map(m=>invoke('GTE.GMatrix',m,[r('a')])),
      invoke('GTE.GMatrix','Inverse',[r('a'),out('invertible')],['GTE.GMatrix','Boolean&']),call('a','GetRow',[i(0)]),call('a','SetRow',[i(0),r('v')]),call('a','SetCol',[i(n-1),r('v')]),snapshot('a'),call('a','MakeIdentity'),snapshot('a')],rowMajor);
  }
  for(const rowMajor of [true,false])for(const rows of [2,3])for(const cols of [2,4])add(`lexico/${rowMajor}/${rows}/${cols}`,'matrices',[
    {kind:'value',id:'storage',value:arr('Double',Array.from({length:rows*cols},(_,j)=>j+1))},create('a','GTE.LexicoArray2',[i(rows),i(cols),r('storage')]),get('a','NumRows'),get('a','NumCols'),
    {kind:'index',target:'a',args:[i(1),i(1)]},setIndex('a',[i(1),i(1)],-5),snapshot('storage'),{kind:'value',id:'copy',value:arr('Double',Array(rows*cols).fill(0))},call('a','CopyTo',[r('copy'),i(0)]),snapshot('copy')],rowMajor);
  for(const n of [2,3,4,6])for(const mode of ['factor','solve','inverse']) {
    const init=[create('a','GTE.BandedMatrix',[i(n),i(1),i(1)])];for(let row=0;row<n;row++){init.push(setIndex('a',[i(row),i(row)],5));if(row+1<n)init.push(setIndex('a',[i(row),i(row+1)],1),setIndex('a',[i(row+1),i(row)],1));}
    init.push(get('a','DBand'),get('a','LBands'),get('a','UBands'));
    if(mode==='factor')init.push(call('a','CholeskyFactor'));
    else if(mode==='solve')init.push({kind:'value',id:'rhs',value:arr('Double',Array.from({length:n},(_,j)=>j+1))},call('a','SolveSystem',[cell('rhs')],['Double[]&']),snapshot('rhs'));
    else init.push({kind:'value',id:'inverse',value:arr('Double',Array(n*n).fill(0))},call('a','ComputeInverse',[r('inverse')]),snapshot('inverse'));
    init.push(get('a','DBand'),get('a','LBands'),get('a','UBands'));add(`banded/${n}/${mode}`,'banded',init);
  }
  for(const n of [1,2,3,4])for(let sample=0;sample<3;sample++)add(`gaussian/${n}/${sample}`,'gaussian',[
    {kind:'value',id:'matrix',value:arr('Double',Array.from({length:n*n},(_,j)=>j%(n+1)===0?sample+2:1))},
    ...['inverse','rhs','result'].map(id=>({kind:'value',id,value:arr('Double',Array(id==='inverse'?n*n:n).fill(id==='inverse'?7:id==='rhs'?2:9))})),
    invoke('GTE.GaussianElimination','Solve',[i(n),r('matrix'),r('inverse'),out('det'),r('rhs'),r('result'),null,i(0),null],['Int32','Double[]','Double[]','Double&','Double[]','Double[]','Double[]','Int32','Double[]']),
    snapshot('inverse'),snapshot('result'),snapshot('matrix')]);
  const polynomials=[[1,0,-1],[1,2,1],[1,0,1],[0,1,-2],[1,-3,2],[1,0,0,0],[1,0,0,-2],[1,-6,11,-6],[1,0,-1,0],[1,0,0,0,-1],[1,-4,6,-4,1],[1,0,-5,0,4],[0,1,-6,11,-6]];
  for(let sample=0;sample<80;sample++){const degree=2+sample%3;polynomials.push(Array.from({length:degree+1},(_,j)=>j===0?1:Math.floor(random()*13)-6));}
  for(const [index,coefficients] of polynomials.entries()){
    const degree=coefficients.length-1,suffix=['','','Quadratic','Cubic','Quartic'][degree],args=coefficients.toReversed();
    add(`roots/${index}`,'polynomials',[invoke('GTE.RootsPolynomial','Solve'+suffix,[...args,out('roots')]),snapshot('roots'),
      invoke('GTE.RootsPolynomial','GetRootInfo'+suffix,[...args,out('multiplicities')]),snapshot('multiplicities'),
      invoke('GTE.RootsPolynomial','Find',[i(degree),arr('Double',args),i(128),out('numeric')],['Int32','Double[]','Int32','Double[]&'])]);
  }
  for(const coefficients of [[1,0,-2],[1,0,0,-2],[1,-0.25],[0,2]])for(const max of [0,1,4,32,128])add(`bisection/${coefficients.join('_')}/${max}`,'integration',[
    invoke('GTE.RootsBisection','Find',[fn(coefficients),-3,3,i(max),out('root')],['DoubleFunction','Double','Double','Int32','Double&']),{kind:'trace'}]);
  for(const coefficients of [[2],[1,0],[1,0,0],[1,-3,2],[1,0,0,0,0]])for(const order of [2,4,8])add(`integration/${coefficients.join('_')}/${order}`,'integration',[
    invoke('GTE.Integration','TrapezoidRule',[i(order*4),-1,2,fn(coefficients)]),invoke('GTE.Integration','Romberg',[i(order),-1,2,fn(coefficients)]),{kind:'trace'}]);
  for(const degree of [2,3,4,6,8])add(`quadrature/${degree}`,'integration',[
    invoke('GTE.Integration','ComputeQuadratureInfo',[i(degree),out('roots'),out('weights')],['Int32','Double[]&','Double[]&']),
    invoke('GTE.Integration','GaussianQuadrature',[r('roots'),r('weights'),-1,2,fn([1,0,0])]),{kind:'trace'}]);
  for(let sample=0;sample<40;sample++){
    const a=Math.floor(random()*10)-5,b=a+Math.floor(random()*6),c0=Math.floor(random()*10)-5,d=c0+Math.floor(random()*6),speed0=2*random()-1,speed1=2*random()-1;
    for(const Type of ['TIQueryIntervals','FIQueryIntervals'])add(`intervals/${Type}/${sample}`,'intervals',[
      create('static','GTE.'+Type,[arr('Double',[a,b]),arr('Double',[c0,d])]),...['Intersect','FirstTime','LastTime',...(Type==='FIQueryIntervals'?['Type','Overlap','NumIntersections']:[])].map(m=>get('static',m)),
      create('dynamic','GTE.'+Type,[5,arr('Double',[a,b]),speed0,arr('Double',[c0,d]),speed1]),...['Intersect','FirstTime','LastTime',...(Type==='FIQueryIntervals'?['Type','Overlap','NumIntersections']:[])].map(m=>get('dynamic',m))]);
  }
  for(const Type of ['BezierCurve','BSplineCurve','NURBSCurve'])for(const n of [4,6])for(let sample=0;sample<6;sample++){
    const degree=Type==='BezierCurve'?n-1:3,points=Array.from({length:n},(_,j)=>v(j,j*j/4,(j%2)*sample));
    const args=Type==='BezierCurve'?[arr('Vector3',points),i(degree)]:[{new:'GTE.BasisFunctionInput',args:[i(n),i(degree)]},arr('Vector3',points),...(Type==='NURBSCurve'?[arr('Double',Array.from({length:n},(_,j)=>1+(sample%3)*j/4))]:[])];
    const steps=[create('curve','GTE.'+Type,args),get('curve','NumControls'),get('curve','Controls'),get('curve','TMin'),get('curve','TMax')];
    for(const t of [-0.125,0,0.125,0.5,0.875,1,1.125])for(const order of [0,1,2,3,4])steps.push(call('curve','Evaluate',[t,i(order),out('jet')],['Double','Int32','Vector3[]&']));
    steps.push(call('curve','GetPosition',[0.25]),call('curve','GetTangent',[0.25]),call('curve','GetSpeed',[0.25]),call('curve','GetTotalLength'),call('curve','SubdivideByTime',[i(5),out('times')],['Int32','Double[]&']));
    if(Type==='NURBSCurve')steps.push(call('curve','SetControl',[i(1),v(9,8,7)]),call('curve','SetWeight',[i(1),3]),get('curve','Controls'),get('curve','Weights'));
    add(`curves/${Type}/${n}/${sample}`,'curves',steps);
  }
  for(const count of [6,8,12])for(const degree of [2,3]){
    const points=arr('Vector3',Array.from({length:count},(_,j)=>v(j,j*j/8,j%2)));
    add(`curve-fit/${count}/${degree}`,'fitting',[create('fit','GTE.BSplineCurveFit',[points,i(degree),i(5)]),...['NumSamples','Degree','NumControls','SampleData','ControlData'].map(m=>get('fit',m)),
      ...[0,0.25,0.5,1].flatMap(t=>[call('fit','GetPosition',[t]),call('fit','Evaluate',[t,i(1),out('tangent')],['Double','Int32','Vector3&'])])]);
    for(const fraction of [0.5,0.8,1])add(`reduction/${count}/${degree}/${fraction}`,'fitting',[create('fit','GTE.BSplineReduction',[points,i(degree),fraction]),...['NumSamples','Degree','NumControls','SampleData','ControlData'].map(m=>get('fit',m))]);
  }
  for(const n of [4,6])for(const degree of [2,3]){
    const points=arr('Vector3',Array.from({length:n*n},(_,j)=>v(j%n,Math.floor(j/n),j%3)));
    add(`surface-fit/${n}/${degree}`,'fitting',[create('fit','GTE.BSplineSurfaceFit',[i(degree),i(4),i(n),i(degree),i(4),i(n),points]),get('fit','ControlData'),get('fit','SampleData'),...[[0,0],[0.25,0.75],[0.5,0.5],[1,1]].map(([u,v])=>call('fit','GetPosition',[u,v]))]);
  }
  // Additional nonrecursive vector construction/storage probes do not replace any
  // source-recursion cases above. Preserve all prior failure evidence.
  for(const size of [0,1,2,3,8])for(const [ordinal,component] of [-1,0,1,size-1,size].entries())add(`vector-storage/${size}/${ordinal}/${component}`,'vector-storage',[
    create('a','GTE.GVector',[i(size),i(component)]),get('a','Size'),get('a','Vector'),call('a','MakeUnit',[i(component)]),snapshot('a'),call('a','MakeZero'),snapshot('a'),
    invoke('GTE.GVector','Zero',[i(size)]),invoke('GTE.GVector','Unit',[i(size),i(component)])]);
  // These inputs remain in the required corpus even when native assertions or
  // recursive source Equals terminate the process. Unavailable evidence is failure.
  add('source/vector-equality','source-failures',[create('a','GTE.GVector',[arr('Double',[1,2])]),create('b','GTE.GVector',[arr('Double',[1,2])]),call('a','Equals',[r('b')],['GTE.GVector'])]);
  add('source/matrix-index','source-failures',[create('a','GTE.GMatrix',[i(2),i(2)]),{kind:'index',target:'a',args:[i(-1),i(0)]}]);
  add('source/quadrature-one','source-failures',[invoke('GTE.Integration','ComputeQuadratureInfo',[i(1),out('roots'),out('weights')],['Int32','Double[]&','Double[]&'])]);
  add('callback/throw','callbacks',[invoke('GTE.Integration','Romberg',[i(4),0,1,fn([1,0,0],3)]),{kind:'trace'}]);
  return all;
}
