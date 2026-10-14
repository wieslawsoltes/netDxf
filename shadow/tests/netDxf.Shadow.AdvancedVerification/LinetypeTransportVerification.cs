using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class LinetypeTransportVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-transport/"+name,run));
        var symbol = new CadLinetypeDecoration(CadLinetypeDecorationKind.Shape,"mark","host");
        var pattern = new CadLinetype([new(1,symbol),new(-1)],1,Vec3.UnitZ);
        var options = new LinetypePlanOptions { FrameMode=LinetypeFrameMode.ParallelTransport };
        Test("spatial line has finite orthonormal placement frames",()=>
        {
            var points=ImmutableArray.Create(Vec3.Zero,new Vec3(10,0,10));
            var placements=LinetypePlanner.Plan([new(points,false)],pattern,options:options);
            Check(placements.Length>4); foreach(var p in placements) Orthonormal(p.Frame);
            Near(placements[0].Frame.Translation.X,Math.Sqrt(.5)); Near(placements[0].Frame.Translation.Z,Math.Sqrt(.5));
        });
        Test("planar transport agrees with existing authored-plane placements",()=>
        {
            ImmutableArray<RenderContour> paths=[new([Vec3.Zero,new(10,0),new(10,10)],false)];
            var expected=LinetypePlanner.Plan(paths,pattern); var actual=LinetypePlanner.Plan(paths,pattern,options:options);
            Check(expected.Length==actual.Length);
            for(int i=0;i<expected.Length;i++){ Near(Vec3.Distance(expected[i].Frame.X,actual[i].Frame.X),0);Near(Vec3.Distance(expected[i].Frame.Y,actual[i].Frame.Y),0); }
        });
        Test("helix follows every tangent without arbitrary frame flips",()=>
        {
            var points=Enumerable.Range(0,501).Select(i=>new Vec3(10*Math.Cos(i*.03),10*Math.Sin(i*.03),i*.02)).ToImmutableArray();
            var transport=LinetypeTransport.Create(points,Vec3.UnitZ); double distance=0; Vec3? previous=null;
            for(int i=0;i<points.Length-1;i++)
            {
                var frame=transport.At(i,distance,points[i]); Orthonormal(frame);
                Near(Vec3.Dot(frame.X,(points[i+1]-points[i]).Normalized),1);
                if(previous is {} side) Check(Vec3.Dot(side,frame.Y)>.99); previous=frame.Y; distance+=Vec3.Distance(points[i],points[i+1]);
            }
        });
        Test("normal parallel to first tangent uses a deterministic seed",()=>
        {
            var a=LinetypeTransport.Create([Vec3.Zero,new(0,0,5)],Vec3.UnitZ).At(0,2,new(0,0,2));
            var b=LinetypeTransport.Create([Vec3.Zero,new(0,0,5)],Vec3.UnitZ).At(0,2,new(0,0,2));
            Orthonormal(a);Check(a==b);
        });
        Test("antiparallel cusp preserves side and flips tangent and normal",()=>
        {
            var transport=LinetypeTransport.Create([Vec3.Zero,Vec3.UnitX,Vec3.Zero],Vec3.UnitZ);
            var a=transport.At(0,.5,new(.5,0));var b=transport.At(1,1.5,new(.5,0));
            Near(Vec3.Dot(a.X,b.X),-1);Near(Vec3.Dot(a.Y,b.Y),1);Near(Vec3.Dot(a.Z,b.Z),-1);
        });
        Test("duplicate vertices consume no extra transport rotation",()=>
        {
            var a=LinetypeTransport.Create([Vec3.Zero,Vec3.UnitX,new(1,0,2)],Vec3.UnitZ).At(1,2,new(1,0,1));
            var b=LinetypeTransport.Create([Vec3.Zero,Vec3.Zero,Vec3.UnitX,Vec3.UnitX,new(1,0,2)],Vec3.UnitZ).At(3,2,new(1,0,1));
            Check(a==b);
        });
        Test("closed loop distributes residual roll with finite frames",()=>
        {
            ImmutableArray<Vec3> points=[new(1,0,0),new(0,1,1),new(-1,0,-.5),new(0,-1,1),new(1,0,0)];
            var transport=LinetypeTransport.Create(points,Vec3.UnitZ,true); double distance=0;
            for(int i=0;i<points.Length-1;i++){Orthonormal(transport.At(i,distance,points[i]));distance+=Vec3.Distance(points[i],points[i+1]);}
            Orthonormal(transport.At(points.Length-2,distance,points[^1]));
            var explicitResult=LinetypePlanner.Plan([new(points,true)],pattern,options:options);
            var implicitResult=LinetypePlanner.Plan([new(points.RemoveAt(points.Length-1),true)],pattern,options:options);
            Check(explicitResult.SequenceEqual(implicitResult));
        });
        Test("A fitting and 3D transport share one event schedule",()=>
        {
            var placements=LinetypePlanner.Plan([new([Vec3.Zero,new(10,0,10)],false)],pattern,CadLinetypeAlignment.A,options:options);
            Check(placements.Length>0);foreach(var p in placements)Orthonormal(p.Frame);
        });
        Test("render expansion remains presentation only on spatial paths",()=>
        {
            var occurrence=new CadOccurrence("line",new("test:line"),[],CadStyle.Default with { Dashes=[1,-1],Linetype=pattern },new([new([new LineCurve(Vec3.Zero,new(10,0,10))])]));
            var scene=RenderCompiler.Compile(new(Guid.NewGuid(),0,"Model",[occurrence],[]),linetypeResources:new Marker(),linetypeOptions:options);
            Check(scene.Items.Length>4 && scene.Items.Skip(1).All(i=>i.IsPresentationOnly));Check(!scene.Diagnostics.Any(d=>d.Severity==DiagnosticSeverity.Error));
        });
        Test("legacy planar policy still rejects spatial path explicitly",()=>Throws(()=>LinetypePlanner.Plan([new([Vec3.Zero,new(10,0,10)],false)],pattern)));
        Test("zero paths are rejected",()=>Throws(()=>LinetypeTransport.Create([Vec3.Zero,Vec3.Zero],Vec3.UnitZ)));
        Test("invalid seed is rejected",()=>Throws(()=>LinetypeTransport.Create([Vec3.Zero,Vec3.UnitX],Vec3.Zero)));
        Test("closed endpoint addition obeys point quota",()=>Throws(()=>LinetypeTransport.Create([Vec3.Zero,Vec3.UnitX],Vec3.UnitZ,true,maximumPoints:2)));
        Test("cancellation precedes any path work",()=>Throws<OperationCanceledException>(()=>LinetypeTransport.Create([Vec3.Zero,Vec3.UnitX],Vec3.UnitZ,cancellationToken:new(true))));
        Test("invalid frame option is rejected",()=>Throws(()=>LinetypePlanner.Plan([new([Vec3.Zero,Vec3.UnitX],false)],pattern,options:new(){FrameMode=(LinetypeFrameMode)99})));
    }
    private sealed class Marker:ILinetypeDecorationProvider
    {
        public long Revision=>0;
        public bool TryProject(CadLinetypeDecoration decoration,out CadGeometry geometry,out string? diagnostic)
        {geometry=new([new([new LineCurve(Vec3.Zero,Vec3.UnitY)])]);diagnostic=null;return true;}
    }
    private static void Orthonormal(Affine3 f){Check(f.IsFinite);Near(f.X.Length,1);Near(f.Y.Length,1);Near(f.Z.Length,1);Near(Vec3.Dot(f.X,f.Y),0);Near(Vec3.Distance(Vec3.Cross(f.X,f.Y),f.Z),0);}
    private static void Near(double a,double b){Check(double.IsFinite(a)&&Math.Abs(a-b)<1e-8);}
    private static void Check(bool c){if(!c)throw new Exception("Spatial transport contract failed.");}
    private static void Throws(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){return;}throw new Exception("Expected explicit refusal.");}
    private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
