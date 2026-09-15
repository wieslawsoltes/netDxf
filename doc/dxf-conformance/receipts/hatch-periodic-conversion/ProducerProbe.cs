using netDxf; using netDxf.Entities; using System.Text.Json;
var fixture=args.Length>0 ? args[0] : "/workspace/scratch/2ec4aa26f01f/recovered-hatch-periodic/tests/fixtures/hatch-spline-relations/ezdxf-hatch-spline-R2018-ascii.dxf";
var doc=DxfDocument.Load(fixture);var rows=new List<object>();
foreach(var h in doc.Entities.Hatches){var edge=h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();string error=null;int count=0;try{var s=(Spline)edge.ConvertTo();count=s.PolygonalVertexes(17).Count;}catch(Exception e){error=e.GetType().Name+": "+e.Message;}rows.Add(new{periodic=edge.IsPeriodic,controls=edge.ControlPoints.Length,knots=edge.Knots.Length,degree=edge.Degree,error,count});}
var periodic=doc.Entities.Hatches.First(h=>h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single().IsPeriodic);var packet=periodic.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
var compact=new Spline(packet.ControlPoints.Skip(packet.Degree).Select(p=>new Vector3(p.X,p.Y,0)),packet.ControlPoints.Skip(packet.Degree).Select(p=>p.Z),packet.Knots,packet.Degree,true);
var back=new HatchBoundaryPath.Spline(compact);var samples=compact.PolygonalVertexes(17).Select(v=>new[]{v.X,v.Y,v.Z}).ToArray();
string boundaryError=null;try{periodic.CreateBoundary(true);}catch(Exception e){boundaryError=e.GetType().Name+": "+e.Message;}
Console.WriteLine(JsonSerializer.Serialize(new{sourceDll="c6acdbb4e5b1d7fc0a4751fb5addef838096af27bb1ead6184f4aa9abcf39d1b",rows,adapter=new{compactControls=compact.ControlPoints.Length,edgeControls=back.ControlPoints.Length,edgeKnots=back.Knots.Length,back.IsPeriodic,samples},boundaryFailure=new{boundaryError,periodic.Associative}},new JsonSerializerOptions{WriteIndented=true}));
