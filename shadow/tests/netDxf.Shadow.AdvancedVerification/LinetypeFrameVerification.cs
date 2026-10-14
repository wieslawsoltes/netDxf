using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class LinetypeFrameVerification
{
    private static readonly CadLinetypeDecoration Text = new(CadLinetypeDecorationKind.Text,"A","");
    private static readonly LinetypePlanOptions Definition = new() { MetricSpace=LinetypeMetricSpace.Definition };
    public static void Register(List<(string Name,Action Run)> tests)
    {
        void Test(string name,Action run)=>tests.Add(("linetype-frames/"+name,run));
        foreach(var angle in new[]{0d,30,90,180,270})
        Test("nominal cap center remains fixed/"+angle,()=>{
            var decoration=Text with{Rotation=angle};
            var plan=Plan(decoration,new(){TextRotationPivot=LinetypeTextRotationPivot.NominalCapHeightCenter});
            Vector(plan[0].Frame.Point(new(0,.5)),new(3,.5));
        });
        Test("style oblique and upside-down axes determine nominal cap center",()=>{
            var decoration=Text with{Rotation=130,ObliqueAngle=20,IsUpsideDown=true,Scale=2};
            var pivot=decoration.CreateText().YAxis*.5;
            var placement=Plan(decoration,new(){TextRotationPivot=LinetypeTextRotationPivot.NominalCapHeightCenter})[0];
            Vector(placement.Frame.Point(pivot),new Vec3(3,0)+pivot*2);
        });
        Test("upright reversal rotates about nominal cap center not baseline",()=>{
            var decoration=Text with{RotationType=CadLinetypeRotation.Upright};
            var plan=LinetypePlanner.Plan([new([new(10,0),Vec3.Zero],false)],Pattern(decoration),
                options:new(){TextRotationPivot=LinetypeTextRotationPivot.NominalCapHeightCenter});
            Vector(plan[0].Frame.Point(new(0,.5)),new(7,-.5));
            Vector(plan[0].Frame.X,Vec3.UnitX);
        });
        Test("explicit pivot is applied without changing tangent-relative offsets",()=>{
            var placement=Plan(Text with{Rotation=90,Offset=new(2,1)},new(){TextPivot=new(1,2)})[0];
            Vector(placement.Frame.Point(new(1,2)),new(6,3));
        });
        Test("shape insertion origin is not changed by text pivot policy",()=>{
            var shape=Text with{Kind=CadLinetypeDecorationKind.Shape,Rotation=90};
            Vector(Plan(shape,new(){TextRotationPivot=LinetypeTextRotationPivot.NominalCapHeightCenter})[0].Frame.Translation,new(3,0));
        });
        foreach(var transform in new[]
        {
            Affine3.Scale(new(2,3,1)), Affine3.Scale(new(-2,3,1)),
            new Affine3(new(2,1),new(.7,3),Vec3.UnitZ,new(100,-20)),
            Affine3.Move(new(30,40,2))*Affine3.RotateZ(.7)*Affine3.Scale(new(3,-.5,2))
        })
        Test("definition metric commutes with affine instance/"+transform,()=>{
            ImmutableArray<RenderContour> local=[new([Vec3.Zero,new(10,0),new(10,10)],false)];
            var pattern=Pattern(Text with{Rotation=37,Offset=new(.5,.2)});
            var expected=LinetypePlanner.Plan(local,pattern,options:Definition);
            var world=local.Select(c=>c with{Points=c.Points.Select(transform.Point).ToImmutableArray()}).ToImmutableArray();
            var actual=LinetypePlanner.Plan(world,pattern,transform,Definition);
            Check(actual.Length==expected.Length);
            for(var i=0;i<actual.Length;i++){
                Vector(actual[i].Frame.Translation,transform.Point(expected[i].Frame.Translation));
                Vector(actual[i].Frame.X,transform.Direction(expected[i].Frame.X));
                Vector(actual[i].Frame.Y,transform.Direction(expected[i].Frame.Y));
                Near(actual[i].Distance,expected[i].Distance);
            }
        });
        Test("definition dashes preserve period through nonuniform corners",()=>{
            var transform=Affine3.Scale(new(2,3,1));
            ImmutableArray<Vec3> points=[Vec3.Zero,new(10,0),new(10,10)];
            var item=new RenderItem("line",new("test"),[],CadStyle.Default with{Dashes=[2,-2]},
                [new(points.Select(transform.Point).ToImmutableArray(),false)],false,null,null,new(0,0,20,30)){PatternToWorld=transform};
            var expected=StrokePattern.Apply([new(points,false)],[2,-2]);
            var actual=StrokePattern.GetContours(item);
            Check(actual.Length==expected.Length);
            for(var i=0;i<actual.Length;i++) for(var j=0;j<actual[i].Points.Length;j++)
                Vector(actual[i].Points[j],transform.Point(expected[i].Points[j]));
        });
        Test("sampled arc glyphs preserve complete affine frames",()=>{
            var transform=new Affine3(new(2,.2),new(.8,4),Vec3.UnitZ,new(2,8));
            var points=CurveSampler.Sample(new EllipseCurve(Vec3.Zero,Vec3.UnitX*10,Vec3.UnitY*10,0,Math.PI),new(.01));
            var expected=LinetypePlanner.Plan([new(points,false)],Pattern(Text),options:Definition);
            var actual=LinetypePlanner.Plan([new(points.Select(transform.Point).ToImmutableArray(),false)],Pattern(Text),transform,Definition);
            Check(actual.Length==expected.Length);
            for(var i=0;i<actual.Length;i++)Vector(actual[i].Frame.Y,transform.Direction(expected[i].Frame.Y));
        });
        Test("shared compiler dashes text and graph preserve nonuniform instance",()=>{
            var (session,insert)=Drawing();var compiler=new RenderCompiler(linetypeOptions:Definition);
            var scene=compiler.Compile(session.Current);
            var source=scene.Items.Single(i=>!i.IsPresentationOnly);
            Near(Vec3.Distance(StrokePattern.GetContours(source)[0].Points[0],StrokePattern.GetContours(source)[0].Points[^1]),4);
            var symbol=scene.Items.First(i=>i.IsPresentationOnly);
            Near(symbol.Text!.XAxis.Length,2);Near(symbol.Text.YAxis.Length,3);
            Check(symbol.Entity==source.Entity && symbol.InsertPath.SequenceEqual(source.InsertPath));
            Check(new TopologyBuilder().Build(session.Current).Edges.Length==1);
        });
        Test("definition symbols and strokes receive paper compensation exactly once",()=>{
            var(session,insert)=Drawing();
            session.MutateSource(doc=>{
                doc.DrawingVariables.PsLtScale=0;var sheet=new Layout("Sheet");doc.Layouts.Add(sheet);
                sheet.AssociatedBlock.Entities.Add(new netDxf.Entities.Viewport{Width=100,Height=100,ViewHeight=200,ViewDirection=Vector3.UnitZ,Stacking=2});
            });
            var scene=new RenderCompiler(linetypeOptions:Definition).Compile(session.Current,new("Sheet"));
            var source=scene.Items.First(i=>i.Projection is not null&&!i.IsPresentationOnly&&i.Contours.Length>0);
            Near(Vec3.Distance(source.Contours[0].Points[0],source.Contours[0].Points[^1]),2);
            Check(source.PatternToWorld == Affine3.Identity && source.Style.Dashes.IsEmpty && source.Style.Linetype is null);
            var symbol=scene.Items.First(i=>i.IsPresentationOnly);
            Near(symbol.Text!.XAxis.Length,1);Near(symbol.Text.YAxis.Length,1.5);
        });
        Test("singular metric frame is rejected without falling back",()=>{
            var transform=Affine3.Scale(new(0,1,1));
            Throws<ArgumentException>(()=>LinetypePlanner.Plan([new([Vec3.Zero,new(10,0)],false)],Pattern(Text),transform,Definition));
        });
        Test("invalid pivot and metric enum reject at construction",()=>{
            Throws<ArgumentException>(()=>new RenderCompiler(linetypeOptions:new(){MetricSpace=(LinetypeMetricSpace)99}));
            Throws<ArgumentException>(()=>new RenderCompiler(linetypeOptions:new(){TextPivot=new(double.NaN,0)}));
            Throws<ArgumentException>(()=>new RenderCompiler(linetypeOptions:new(){TextPivot=Vec3.UnitZ}));
        });
        Test("definition coordinate preflight and cancellation preserve quotas",()=>{
            Throws<InvalidOperationException>(()=>LinetypePlanner.Plan([new([Vec3.Zero,Vec3.UnitX,new(2,0)],false)],Pattern(Text),
                Affine3.Scale(new(2,2,2)),Definition with{MaximumPoints=2}));
            Throws<OperationCanceledException>(()=>LinetypePlanner.Plan([],Pattern(Text),Affine3.Scale(new(2,2,2)),Definition,new(true)));
        });
    }
    private static ImmutableArray<LinetypePlacement> Plan(CadLinetypeDecoration value,LinetypePlanOptions options)
        =>LinetypePlanner.Plan([new([Vec3.Zero,new(10,0)],false)],Pattern(value),options:options);
    private static CadLinetype Pattern(CadLinetypeDecoration value)=>new([new(2),new(-1,value),new(-1)],1,Vec3.UnitZ);
    private static (ShadowSession,Insert) Drawing(){
        var type=new Linetype("FRAMES",new LinetypeSegment[]{new LinetypeSimpleSegment(2),new LinetypeTextSegment("A",new TextStyle("FRAME_TEXT","Arial.ttf"),-1),new LinetypeSimpleSegment(-1)});
        var block=new Block("FRAME_BLOCK");block.Entities.Add(new Line(Vector3.Zero,new Vector3(10,0,0)){Linetype=type});
        var insert=new Insert(block,Vector3.Zero){Scale=new(2,3,1)};var doc=new DxfDocument();doc.Entities.Add(insert);return(new(doc),insert);
    }
    private static void Vector(Vec3 a,Vec3 b)=>Near(Vec3.Distance(a,b),0);
    private static void Near(double a,double b)=>Check(double.IsFinite(a)&&Math.Abs(a-b)<1e-7,$"Expected {b}; got {a}");
    private static void Check(bool value,string message="Frame assertion failed"){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
}
