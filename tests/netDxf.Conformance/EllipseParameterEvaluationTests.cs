// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly double[] EllipseEvaluationParameters = { 0, Math.PI/6, Math.PI/2, Math.PI, 3*Math.PI/2, 5*Math.PI/3, 2*Math.PI };
    private static readonly Vector3[] EllipseEvaluationNormals = { Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitZ };

    private static Ellipse EvaluationEllipse(int plane, int rotation, double scale = 1)
        => new Ellipse(new Vector3(1, 2, 3) * scale, 12 * scale, 4 * scale) {
            Normal = EllipseEvaluationNormals[plane], Rotation = rotation * 45,
            StartAngle = 25, EndAngle = 26, Thickness = 2, ProxyGraphics = new byte[] { 1, 4, 9 }
        };

    private static double[][] ExpectedEllipseDerivatives(int plane, int rotation, double parameter, double scale)
    {
        // Independent explicit bases for +Z, +X and -Z; no production OCS or
        // parameter evaluator is used for these expectations.
        double[][] x = { new[]{1.0,0.0,0.0}, new[]{0.0,1.0,0.0}, new[]{-1.0,0.0,0.0} };
        double[][] y = { new[]{0.0,1.0,0.0}, new[]{0.0,0.0,1.0}, new[]{0.0,1.0,0.0} };
        double angle = rotation * Math.PI / 4, c = Math.Cos(angle), s = Math.Sin(angle);
        double[] a = Enumerable.Range(0,3).Select(i => 6*scale*(x[plane][i]*c+y[plane][i]*s)).ToArray();
        double[] b = Enumerable.Range(0,3).Select(i => 2*scale*(-x[plane][i]*s+y[plane][i]*c)).ToArray();
        double ct = Math.Cos(parameter), st = Math.Sin(parameter);
        if (parameter == 0 || parameter == 2*Math.PI) { ct=1;st=0; }
        if (parameter == Math.PI/2) { ct=0;st=1; }
        if (parameter == Math.PI) { ct=-1;st=0; }
        if (parameter == 3*Math.PI/2) { ct=0;st=-1; }
        var values = new double[11][];
        for (int k=0;k<values.Length;k++)
        {
            double u = k%4==0 ? ct : k%4==1 ? -st : k%4==2 ? -ct : st;
            double v = k%4==0 ? st : k%4==1 ? ct : k%4==2 ? -st : -ct;
            values[k]=Enumerable.Range(0,3).Select(i => a[i]*u+b[i]*v+(k==0?(i+1)*scale:0)).ToArray();
        }
        return values;
    }

    private static void RegisterEllipseParameterEvaluationTests()
    {
        foreach (double scale in new[]{1e-200,1.0,1e200}) for (int plane=0;plane<3;plane++)
            for (int rotation=0;rotation<3;rotation++) foreach (double parameter in EllipseEvaluationParameters)
            {
                int p=plane,r=rotation;
                Run($"ellipse-parameter/locus/{ParameterBits(scale)}/{p}/{r}/{ParameterBits(parameter)}",()=>
                {
                    var e=EvaluationEllipse(p,r,scale); var owner=new DxfDocument(); owner.Entities.Add(e);
                    string handle=e.Handle; var entityOwner=e.Owner; var color=e.Color; var before=SafeEllipseState(e,e.Normal); var proxy=e.ProxyGraphics!;
                    var actual=e.EvaluateDerivatives(parameter,10); var expected=ExpectedEllipseDerivatives(p,r,parameter,scale);
                    Equal(11,actual.Length,"Derivative count");
                    for(int k=0;k<11;k++) for(int i=0;i<3;i++)
                        Check(double.IsFinite(actual[k][i])&&Math.Abs(actual[k][i]/scale-expected[k][i]/scale)<3e-13,"Independent derivative differs");
                    RawLinePointBits(actual[0],e.PointAt(parameter));
                    for(int k=5;k<11;k++) RawLinePointBits(actual[k-4],actual[k]);
                    Check(before.SequenceEqual(SafeEllipseState(e,e.Normal))&&e.ProxyGraphics!.SequenceEqual(proxy),"Read-only evaluation mutated geometry/cache");
                    Check(e.Handle==handle&&ReferenceEquals(e.Owner,entityOwner)&&ReferenceEquals(e.Color,color),"Evaluation changed ownership/metadata");
                    var saved=actual[0]; actual[0]=new(999,999,999); RawLinePointBits(saved,e.PointAt(parameter));
                });
            }
        foreach(int order in new[]{0,1,2,3,4,5,6,7,8,9,10})
            Run($"ellipse-parameter/order/{order}",()=>
            {
                var e=EvaluationEllipse(0,0);var all=e.EvaluateDerivatives(.3,10);var selected=e.EvaluateDerivatives(.3,order);
                Equal(order+1,selected.Length,"Selected order length");for(int i=0;i<=order;i++)RawLinePointBits(all[i],selected[i]);
                Equal(2,e.EvaluateDerivatives(.3).Length,"Default order");
            });
        foreach(double bad in new[]{double.NaN,double.NegativeInfinity,double.PositiveInfinity,-double.Epsilon,Math.BitIncrement(2*Math.PI),double.MaxValue})
            Run("ellipse-parameter/reject-parameter/"+ParameterBits(bad),()=>
            {
                var e=EvaluationEllipse(0,0);Throws<ArgumentOutOfRangeException>(()=>e.PointAt(bad));Throws<ArgumentOutOfRangeException>(()=>e.EvaluateDerivatives(bad));
                Check(e.ProxyGraphics!.SequenceEqual(new byte[]{1,4,9}),"Rejected parameter changed proxy");
            });
        foreach(int bad in new[]{int.MinValue,-1,11,int.MaxValue})
            Run($"ellipse-parameter/reject-order/{bad}",()=>Throws<ArgumentOutOfRangeException>(()=>EvaluationEllipse(0,0).EvaluateDerivatives(0,bad)));
        foreach(string field in new[]{"center","majorAxis","minorAxis","rotation"}) foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Run($"ellipse-parameter/reject-source/{field}/{ParameterBits(bad)}",()=>
            {
                var e=EvaluationEllipse(0,0); typeof(Ellipse).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(e,field=="center"?(object)new Vector3(bad,0,0):bad);
                Throws<ArgumentException>(()=>e.EvaluateDerivatives(0));Check(e.ProxyGraphics!.SequenceEqual(new byte[]{1,4,9}),"Invalid source cache changed");
            });
        Run("ellipse-parameter/unsupported-semi-axis",()=>Throws<NotSupportedException>(()=>new Ellipse(Vector3.Zero,double.Epsilon,double.Epsilon).PointAt(0)));
        Run("ellipse-parameter/local-underflow",()=>Throws<NotSupportedException>(()=>new Ellipse(Vector3.Zero,8*double.Epsilon,4*double.Epsilon).PointAt(1e-300)));
        Run("ellipse-parameter/output-overflow",()=>Throws<NotSupportedException>(()=>new Ellipse(new Vector3(double.MaxValue,0,0),double.MaxValue,2).PointAt(0)));
        Run("ellipse-parameter/cancellation",()=>RawLinePointBits(Vector3.Zero,new Ellipse(new Vector3(double.MaxValue/2,0,0),double.MaxValue,2).PointAt(Math.PI)));
        Run("ellipse-parameter/extreme-aspect-cardinals",()=>
        {
            var e=new Ellipse(Vector3.Zero,1e308,1e-300);RawLinePointBits(new(0,5e-301,0),e.PointAt(Math.PI/2));
            RawLinePointBits(new(-5e307,0,0),e.EvaluateDerivatives(Math.PI/2)[1]);
        });
        Run("ellipse-parameter/supporting-curve-not-trim",()=>
        {
            var e=EvaluationEllipse(0,0);var expected=e.PointAt(Math.PI);e.StartAngle=double.NaN;e.EndAngle=double.NaN;e.Thickness=double.NaN;
            RawLinePointBits(expected,e.PointAt(Math.PI));
        });
        for(int mode=0;mode<6;mode++)
        {
            int m=mode;Run($"ellipse-parameter/normal-callback/{m}",()=>
            {
                var e=SafeEllipse(m,true);var expected=((Ellipse)e.Clone()).EvaluateDerivatives(.3,10);e.Probe.Armed=true;
                var actual=e.EvaluateDerivatives(.3,10);Equal(0,e.Probe.Reads+e.Probe.Writes,"Evaluation called derived Normal");
                for(int i=0;i<=10;i++)RawLinePointBits(expected[i],actual[i]);
            });
        }
        foreach(double epsilon in new[]{1e-12,1.0,100.0})
            Run($"ellipse-parameter/epsilon/{epsilon}",()=>
            {
                var e=EvaluationEllipse(1,1);var expected=e.EvaluateDerivatives(.3,10);double previous=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;var actual=e.EvaluateDerivatives(.3,10);for(int i=0;i<=10;i++)RawLinePointBits(expected[i],actual[i]);}
                finally{MathHelper.Epsilon=previous;}
            });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})for(int plane=0;plane<3;plane++)
        {
            int p=plane;Run($"ellipse-parameter/wire/{version}/{binary}/{p}",()=>
            {
                var e=EvaluationEllipse(p,1);e.StartAngle=0;e.EndAngle=0;e.Thickness=0;e.ProxyGraphics=null;
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(e);using var output=new MemoryStream();Check(doc.Save(output,binary),"Evaluation source save");
                string stem=$"ellipse-parameter-{version}-{binary}-{p}";File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+".dxf"),output.ToArray());
                File.WriteAllText(Path.Combine(ArtifactDirectory,stem+".json"),JsonSerializer.Serialize(EllipseEvaluationParameters.Select(t=>new{parameter=t,derivatives=e.EvaluateDerivatives(t,10).Select(v=>v.ToArray()).ToArray()})));
                output.Position=0;var loaded=DxfDocument.Load(output)!;Equal(0,loaded.Objects.Validate().Count,"Evaluation fixture graph");
            });
        }
    }
}
