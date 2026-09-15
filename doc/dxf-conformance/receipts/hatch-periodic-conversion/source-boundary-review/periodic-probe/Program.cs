using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;

var results=new List<object>();int failures=0;
foreach(bool end in new[]{false,true})
{
    var source=new Spline(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,0)},new[]{1.0,1.0,1.0,1.0},(short)2,true){Normal=new Vector3(1,1,1)};
    var tangent=new Vector3(-double.MaxValue,-double.MaxValue,double.MaxValue);if(end)source.EndTangent=tangent;else source.StartTangent=tangent;
    bool rejected=false;Vector2? actual=null;string error="";try{var edge=new HatchBoundaryPath.Spline(source);actual=end?edge.EndTangent:edge.StartTangent;}catch(Exception exception){rejected=true;error=exception.GetType().Name;}
    bool finite=actual.HasValue&&double.IsFinite(actual.Value.X)&&double.IsFinite(actual.Value.Y);bool unchanged=(end?source.EndTangent:source.StartTangent)==tangent;
    if(!rejected||!unchanged)failures++;
    results.Add(new{kind=end?"end-tangent":"start-tangent",rejected,returnedFinite=finite,returnedX=actual?.X.ToString("R"),returnedY=actual?.Y.ToString("R"),sourceUnchanged=unchanged,error});
}
long Seed(DxfDocument doc)=>Convert.ToInt64(typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(doc));
foreach(bool associated in new[]{false,true})foreach(bool link in new[]{false,true})foreach(bool overflow in new[]{false,true})
{
    string name=$"atomic-{associated}-{link}-{overflow}";bool passed=false;string error="";
    try
    {
        var points=overflow?new[]{new Vector3(double.MaxValue,double.MaxValue,0),Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,0)}:new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,0)};
        var edge=new HatchBoundaryPath.Spline(new Spline(points,new[]{1.0,1.0,1.0,1.0},(short)2,true));if(!overflow)edge.Knots[0]-=.125;
        var line=new Line(Vector3.Zero,Vector3.UnitX);var first=new HatchBoundaryPath(new EntityObject[]{line});var hatch=new Hatch(HatchPattern.Solid,new[]{first},associated);hatch.BoundaryPaths.Add(new HatchBoundaryPath(new HatchBoundaryPath.Edge[]{edge}));if(overflow)hatch.Normal=new Vector3(1,1,1);
        var doc=new DxfDocument();doc.Entities.Add(hatch);var objects=doc.Entities.All.ToArray();var sources=first.Entities.ToArray();var reactors=line.Reactors.ToArray();var paths=hatch.BoundaryPaths.ToArray();var controls=edge.ControlPoints.ToArray();var knots=edge.Knots.ToArray();long seed=Seed(doc);int callbacks=0;hatch.HatchBoundaryPathAdded+=(_,_)=>callbacks++;hatch.HatchBoundaryPathRemoved+=(_,_)=>callbacks++;
        bool rejected=false;try{hatch.CreateBoundary(link);}catch(Exception exception)when(exception is ArgumentException or NotSupportedException){rejected=true;}
        passed=rejected&&hatch.Associative==associated&&objects.SequenceEqual(doc.Entities.All)&&sources.SequenceEqual(first.Entities)&&reactors.SequenceEqual(line.Reactors)&&paths.SequenceEqual(hatch.BoundaryPaths)&&controls.SequenceEqual(edge.ControlPoints)&&knots.SequenceEqual(edge.Knots)&&seed==Seed(doc)&&callbacks==0;
    }catch(Exception exception){error=exception.ToString();}
    if(!passed)failures++;results.Add(new{kind=name,passed,error});
}
foreach(bool invalid in new[]{false,true})
{
    bool passed=false;string error="";try
    {
        var controls=new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new Vector3(1,1,0)};var knots=Enumerable.Range(0,9).Select(i=>i*1e-150).ToArray();if(invalid)knots[0]-=1e-156;var snapshot=knots.ToArray();bool rejected=false;List<Vector3>? points=null;
        try{points=Spline.NurbsEvaluator(controls,new[]{1.0,1.0,1.0,1.0},knots,2,false,true,16);}catch(NotSupportedException){rejected=true;}
        passed=rejected==invalid&&knots.SequenceEqual(snapshot)&&(invalid||points is {Count:16}&&points.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&p.X>=-1e-12&&p.X<=1+1e-12&&p.Y>=-1e-12&&p.Y<=1+1e-12));
    }catch(Exception exception){error=exception.ToString();}
    if(!passed)failures++;results.Add(new{kind="tiny-period-"+invalid,passed,error});
}
Directory.CreateDirectory(args[0]);File.WriteAllText(Path.Combine(args[0],"results.json"),JsonSerializer.Serialize(new{cases=results.Count,failures,librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Spline).Assembly.Location))).ToLowerInvariant(),results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(results.Count+" cases; "+failures+" failed.");Environment.ExitCode=failures==0?0:1;
