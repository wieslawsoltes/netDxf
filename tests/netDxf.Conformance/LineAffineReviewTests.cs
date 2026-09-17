using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] LineReviewModes = { "identity", "translate", "uniform", "nonuniform", "negative", "shear", "mirror", "rotate", "planar", "collapse", "tiny", "huge" };
    private static Matrix3 LineReviewMatrix(string mode) => mode switch
    {
        "identity" or "translate" => Matrix3.Identity,
        "uniform" => Matrix3.Scale(2), "nonuniform" => Matrix3.Scale(2, 3, 4),
        "negative" => Matrix3.Scale(-2, -3, -4), "shear" => new(1, 2, 3, 0, 1, .5, 0, 0, 1),
        "mirror" => Matrix3.Scale(-1, 1, 1), "rotate" => new(0, -1, 0, 1, 0, 0, 0, 0, 1),
        "planar" => Matrix3.Scale(1, 1, 0), "collapse" => Matrix3.Scale(0),
        "tiny" => Matrix3.Scale(1e-200), "huge" => Matrix3.Scale(1e200),
        _ => throw new ArgumentException(mode)
    };
    private static Vector3 LineReviewTranslation(string mode) => mode is "translate" or "collapse" ? new(11, -7, 13) : Vector3.Zero;
    private static Matrix4 LineReviewMatrix4(Matrix3 a, Vector3 t) => new(a.M11,a.M12,a.M13,t.X, a.M21,a.M22,a.M23,t.Y, a.M31,a.M32,a.M33,t.Z, 0,0,0,1);
    private static Line LineReviewSubject(int normal, int thickness)
    {
        var line = new Line(new Vector3(3, -4, 5), new Vector3(7, 2, -11))
        {
            Normal = new[] { Vector3.UnitZ, Vector3.UnitX, new Vector3(1, 2, 3) }[normal],
            Thickness = (thickness - 1) * 1.75, IsVisible = false, Color = new AciColor(3),
            Layer = new Layer("LINE_AFFINE_LAYER"), LinetypeScale = 1.75,
            ProxyGraphics = new byte[] { 7, 31, 83, (byte)normal, (byte)thickness }
        };
        var data = new XData(new ApplicationRegistry("LINE_AFFINE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"line-{normal}-{thickness}")); line.XData.Add(data);
        return line;
    }
    private static void RegisterLineAffineReviewTests()
    {
        foreach (string mode in LineReviewModes)
        foreach (int normal in Enumerable.Range(0, 3))
        foreach (int thickness in Enumerable.Range(0, 3))
        {
            string m = mode; int n = normal, t = thickness;
            foreach (bool four in new[] { false, true })
            {
                bool f = four;
                Run($"line-affine/model/{m}/{n}/{t}/{f}", () => LineReviewModel(m, n, t, f));
            }
            foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"line-affine/wire/{m}/{n}/{t}/{v}/{b}", () => LineReviewWire(m,n,t,v,b));
            }
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            double bad = invalid; string label = double.IsNaN(bad) ? "nan" : bad > 0 ? "positive-infinity" : "negative-infinity";
            foreach (int index in Enumerable.Range(0, 9))
            { int i = index; Run($"line-affine/reject/matrix3/{i}/{label}", () => { var a=Matrix3.Identity; a[i/3,i%3]=bad; LineReviewReject(LineReviewSubject(0,2), () => a, Vector3.Zero); }); }
            foreach (int index in Enumerable.Range(0, 16))
            { int i = index; Run($"line-affine/reject/matrix4/{i}/{label}", () => { var a=Matrix4.Identity; a[i/4,i%4]=bad; var line=LineReviewSubject(0,2); LineReviewRollback(line, () => line.TransformBy(a)); }); }
            foreach (int index in Enumerable.Range(0, 3))
            { int i = index; Run($"line-affine/reject/translation/{i}/{label}", () => { var t=Vector3.Zero; t[i]=bad; LineReviewReject(LineReviewSubject(0,2), () => Matrix3.Identity, t); }); }
            foreach (int index in Enumerable.Range(0, 10))
            { int i = index; Run($"line-affine/reject/source/{i}/{label}", () => { var line=LineReviewSubject(0,2); if(i<3) {var p=line.StartPoint;p[i]=bad;line.StartPoint=p;} else if(i<6) {var p=line.EndPoint;p[i-3]=bad;line.EndPoint=p;} else if(i==6) line.Thickness=bad; else {var p=Vector3.UnitZ;p[i-7]=bad;line.Normal=p;} LineReviewReject(line, () => Matrix3.Identity, Vector3.Zero); }); }
        }
        foreach (int index in Enumerable.Range(0,4))
        foreach (double value in new[] { double.Epsilon, 2.0, -1.0 })
        { int i=index; double v=value; Run($"line-affine/reject/projective/{i}/{BitConverter.DoubleToInt64Bits(v):X16}", () => {var a=Matrix4.Identity;a[3,i]=v;var line=LineReviewSubject(0,2);LineReviewRollback(line,()=>line.TransformBy(a));}); }
        foreach (string fault in new[] { "end-overflow", "thickness-overflow", "point-underflow", "thickness-underflow", "normal-underflow" })
        { string f=fault; Run("line-affine/reject/"+f, () => LineReviewFault(f)); }
        foreach (string mode in new[] { "identity-bits", "unchanged", "reverse", "reverse-coincident", "cancellation", "unbounded-normal-magnitude", "minimum-scale", "negative-zero-thickness", "derived-normal" })
        {string m=mode;Run("line-affine/boundary/"+m,()=>LineReviewBoundary(m));}
        Run("line-affine/exact-dot-corpus", LineReviewExactCorpus);
    }
    private static void LineReviewNear(double expected, double actual, string message)
    {
        Check(double.IsFinite(actual), message + " finite");
        if(expected == 0) { Check(Math.Abs(actual) <= double.Epsilon * 8, message + " zero"); return; }
        Check(Math.Abs((actual-expected)/expected) <= 4e-13, $"{message}: expected {expected:R}, actual {actual:R}");
    }
    private static void LineReviewNear(Vector3 expected, Vector3 actual, string message)
    {for(int i=0;i<3;i++) LineReviewNear(expected[i],actual[i],message+"/"+i);}
    private static long[] LineReviewBits(Line line) => new[] {line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z,line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z,line.Normal.X,line.Normal.Y,line.Normal.Z,line.Thickness}.Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static bool LineReviewChanged(Line a, Line b) => !LineReviewBits(a).SequenceEqual(LineReviewBits(b));
    private static void LineReviewAssert(Line before, Line after, Matrix3 matrix, Vector3 translation)
    {
        LineReviewNear(matrix*before.StartPoint+translation,after.StartPoint,"start");
        LineReviewNear(matrix*before.EndPoint+translation,after.EndPoint,"end");
        LineReviewNear(1,after.Normal.Modulus(),"unit normal");
        Vector3 transformed=matrix*before.Normal;
        double scale=Math.Max(Math.Abs(transformed.X),Math.Max(Math.Abs(transformed.Y),Math.Abs(transformed.Z)));
        if(scale==0)
        { Equal(0.0,after.Thickness,"collapsed thickness"); Equal(before.Normal,after.Normal,"unused normal"); }
        else
        {
            double length=(transformed/scale).Modulus();
            LineReviewNear((transformed/scale)/length,after.Normal,"normal direction");
            LineReviewNear((before.Thickness*length)*scale,after.Thickness,"signed thickness");
            LineReviewNear(matrix*(before.Normal*before.Thickness),after.Normal*after.Thickness,"extrusion image");
        }
        Equal(before.Layer.Name,after.Layer.Name,"layer");Equal(before.Color.Index,after.Color.Index,"color");
        Equal(before.IsVisible,after.IsVisible,"visibility");Equal(before.LinetypeScale,after.LinetypeScale,"linetype scale");
        Equal(before.XData["LINE_AFFINE"].XDataRecord[0].Value,after.XData["LINE_AFFINE"].XDataRecord[0].Value,"XData");
        if(LineReviewChanged(before,after)) Check(after.ProxyGraphics == null,"changed LINE retained proxy");
        else Check(before.ProxyGraphics!.SequenceEqual(after.ProxyGraphics!),"unchanged LINE proxy");
    }
    private static void LineReviewModel(string mode,int normal,int thickness,bool four)
    {
        var before=LineReviewSubject(normal,thickness);var after=(Line)before.Clone();
        var document=new DxfDocument();document.Entities.Add(after);string handle=after.Handle;var owner=after.Owner;
        var a=LineReviewMatrix(mode);var t=LineReviewTranslation(mode);
        if(four) after.TransformBy(LineReviewMatrix4(a,t));else after.TransformBy(a,t);
        LineReviewAssert(before,after,a,t);Equal(handle,after.Handle,"handle");Check(ReferenceEquals(owner,after.Owner),"owner");Equal(0,document.Objects.Validate().Count,"graph");
    }
    private static void LineReviewWire(string mode,int normal,int thickness,DxfVersion version,bool binary)
    {
        var before=LineReviewSubject(normal,thickness);var after=(Line)before.Clone();
        after.TransformBy(LineReviewMatrix4(LineReviewMatrix(mode),LineReviewTranslation(mode)));
        LineReviewAssert(before,after,LineReviewMatrix(mode),LineReviewTranslation(mode));
        var document=new DxfDocument(version);document.Entities.Add(before);document.Entities.Add(after);
        using var stream=new MemoryStream();Check(document.Save(stream,binary),"save");
        string name=$"line-affine-review-{mode}-{normal}-{thickness}-{version}-{binary}.dxf";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,name),stream.ToArray());
        stream.Position=0;var loaded=DxfDocument.Load(stream);Check(loaded!=null,"reload");
        var lines=loaded!.Entities.Lines.ToArray();Equal(2,lines.Length,"line count");
        LineReviewAssert(lines[0],lines[1],LineReviewMatrix(mode),LineReviewTranslation(mode));Equal(0,loaded.Objects.Validate().Count,"loaded graph");
    }
    private static void LineReviewRollback(Line line,Action action)
    {
        long[] before=LineReviewBits(line);byte[]? proxy=line.ProxyGraphics;
        var layer=line.Layer;var data=line.XData["LINE_AFFINE"];bool rejected=false;
        try {action();} catch(ArgumentException){rejected=true;} catch(NotSupportedException){rejected=true;} catch(InvalidOperationException){rejected=true;}
        Check(rejected,"invalid LINE transformation accepted");Check(before.SequenceEqual(LineReviewBits(line)),"rejected transform mutated geometry");
        Check(proxy==null ? line.ProxyGraphics==null : proxy.SequenceEqual(line.ProxyGraphics!),"rejected transform mutated proxy");
        Check(ReferenceEquals(layer,line.Layer)&&ReferenceEquals(data,line.XData["LINE_AFFINE"]),"rejected transform mutated metadata");
    }
    private static void LineReviewReject(Line line,Func<Matrix3> matrix,Vector3 translation) => LineReviewRollback(line,()=>line.TransformBy(matrix(),translation));
    private static void LineReviewFault(string fault)
    {
        var line=LineReviewSubject(0,2);var a=Matrix3.Identity;
        switch(fault)
        {
            case "end-overflow": line.EndPoint=new Vector3(double.MaxValue,1,1);a=Matrix3.Scale(2);break;
            case "thickness-overflow":line.Thickness=double.MaxValue;a=Matrix3.Scale(2);break;
            case "point-underflow":line.StartPoint=new Vector3(double.Epsilon,1,1);a=Matrix3.Scale(.25);break;
            case "thickness-underflow":line.Thickness=double.Epsilon;a=Matrix3.Scale(.25);break;
            case "normal-underflow":a=new(1,0,double.Epsilon,0,1,0,0,0,double.MaxValue);line.StartPoint=line.EndPoint=Vector3.Zero;break;
        }
        LineReviewReject(line,()=>a,Vector3.Zero);
    }
    private sealed class LineReviewDerived : Line
    {
        public bool Armed;
        public Vector3 StoredNormal => base.Normal;
        public override Vector3 Normal { get {if(Armed) throw new Exception("virtual normal getter invoked");return base.Normal;} set {if(Armed) throw new Exception("virtual normal setter invoked");base.Normal=value;} }
    }
    private static void LineReviewBoundary(string mode)
    {
        var line=LineReviewSubject(0,2);
        switch(mode)
        {
            case "identity-bits":line.StartPoint=new Vector3(-0.0,2,3);line.Thickness=-0.0;long[] bits=LineReviewBits(line);line.TransformBy(Matrix4.Identity);Check(bits.SequenceEqual(LineReviewBits(line)),"identity bits");Check(line.ProxyGraphics!=null,"identity proxy");break;
            case "unchanged":line.StartPoint=Vector3.UnitX;line.EndPoint=Vector3.UnitX*3;line.TransformBy(Matrix3.Scale(1,2,1),Vector3.Zero);Check(line.ProxyGraphics!=null,"unchanged geometry proxy");break;
            case "reverse":var first=line.StartPoint;var last=line.EndPoint;line.Reverse();Equal(first,line.EndPoint,"reverse end");Equal(last,line.StartPoint,"reverse start");Check(line.ProxyGraphics==null,"reverse proxy");line.Reverse();Equal(first,line.StartPoint,"reverse twice");break;
            case "reverse-coincident":line.EndPoint=line.StartPoint;line.Reverse();Check(line.ProxyGraphics!=null,"coincident reverse proxy");break;
            case "cancellation":line.StartPoint=line.EndPoint=new Vector3(1e308,1e308,1);line.TransformBy(new Matrix3(2,-2,1,0,1,0,0,0,1),new Vector3(7,0,0));Equal(8.0,line.StartPoint.X,"overflow cancellation exact point");break;
            case "unbounded-normal-magnitude":line.StartPoint=line.EndPoint=Vector3.Zero;line.Normal=new Vector3(1,1,1);line.Thickness=1e-308;double d=double.MaxValue;line.TransformBy(new Matrix3(d,d,d,d,d,d,d,d,d),Vector3.Zero);Check(double.IsFinite(line.Thickness)&&line.Thickness>1,"rescaled thickness");LineReviewNear(1,line.Normal.Modulus(),"extreme unit normal");break;
            case "minimum-scale":line.StartPoint=Vector3.Zero;line.EndPoint=Vector3.UnitX;line.Thickness=1;line.TransformBy(Matrix3.Scale(double.Epsilon),Vector3.Zero);Equal(double.Epsilon,line.EndPoint.X,"minimum coordinate");Equal(double.Epsilon,line.Thickness,"minimum thickness");Equal(Vector3.UnitZ,line.Normal,"minimum scale normal");break;
            case "negative-zero-thickness":line.Thickness=-0.0;line.TransformBy(Matrix3.Scale(2),Vector3.Zero);Equal(0.0,line.Thickness,"zero thickness remains zero");break;
            case "derived-normal":var derived=new LineReviewDerived{StartPoint=Vector3.UnitX,EndPoint=Vector3.UnitY,Thickness=3,Armed=true};derived.TransformBy(Matrix3.Scale(2),Vector3.Zero);Equal(6.0,derived.Thickness,"derived thickness");Equal(Vector3.UnitZ,derived.StoredNormal,"derived stored normal");break;
        }
    }
    private static void LineReviewExactCorpus()
    {
        var random=new Random(417293);var rows=new List<object>();
        for(int index=0;index<512;index++)
        {
            double Number(int low,int high)=>Math.ScaleB((random.NextDouble()-.5)*2,random.Next(low,high));
            var p=new Vector3(Number(-400,400),Number(-400,400),Number(-400,400));
            var a=new Matrix3(Number(-200,200),Number(-200,200),Number(-200,200),0,1,0,0,0,1);
            var t=new Vector3(Number(-400,400),0,0);var line=new Line(p,p){Thickness=0};
            line.TransformBy(a,t);Check(double.IsFinite(line.StartPoint.X),"exact dot corpus finite");
            rows.Add(new { matrix=Enumerable.Range(0,9).Select(i=>a[i/3,i%3]).ToArray(),point=new[]{p.X,p.Y,p.Z},translation=new[]{t.X,t.Y,t.Z},result=new[]{line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z} });
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory,"line-affine-exact-dot.json"),JsonSerializer.Serialize(rows));
    }
}
