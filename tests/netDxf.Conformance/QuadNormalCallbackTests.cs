// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed class QuadNormalProbe
    {
        internal int Mode, Reads, Writes, Transforms;
        internal bool Armed;
        internal Vector3 Read(Vector3 stored)
        {
            if (!Armed) return stored;
            Reads++;
            if (Mode == 1) throw new InvalidOperationException("Derived getter must not run during base transformation.");
            return Mode == 2 ? Vector3.UnitX : stored;
        }
        internal void Write(Vector3 value, Action<Vector3> store)
        {
            if (!Armed) { store(value); return; }
            Writes++;
            if (Mode == 3) throw new InvalidOperationException("Derived setter rejects.");
            if (Mode == 4) { store(Vector3.UnitX); throw new InvalidOperationException("Derived setter mutates then throws."); }
            if (Mode == 5) return;
            store(value);
        }
    }
    private interface IQuadNormalProbe
    {
        QuadNormalProbe Probe { get; }
        Vector3 StoredNormal { get; }
    }
    private sealed class CallbackSolid : Solid, IQuadNormalProbe
    {
        public QuadNormalProbe Probe { get; } = new();
        public Vector3 StoredNormal => base.Normal;
        public override Vector3 Normal
        {
            get => Probe.Read(base.Normal);
            set => Probe.Write(value, n => base.Normal = n);
        }
        public override void TransformBy(Matrix3 matrix, Vector3 translation)
        { Probe.Transforms++; base.TransformBy(matrix, translation); }
    }
    private sealed class CallbackTrace : Trace, IQuadNormalProbe
    {
        public QuadNormalProbe Probe { get; } = new();
        public Vector3 StoredNormal => base.Normal;
        public override Vector3 Normal
        {
            get => Probe.Read(base.Normal);
            set => Probe.Write(value, n => base.Normal = n);
        }
        public override void TransformBy(Matrix3 matrix, Vector3 translation)
        { Probe.Transforms++; base.TransformBy(matrix, translation); }
    }
    private static EntityObject CallbackQuad(bool trace, int mode)
    {
        EntityObject e = trace ? new CallbackTrace() : new CallbackSolid();
        if (e is Solid s)
        {
            s.FirstVertex = new(1,2); s.SecondVertex = new(4,3); s.ThirdVertex = new(2,7); s.FourthVertex = new(6,8);
            s.Elevation = 3; s.Thickness = -2;
        }
        else
        {
            var t = (Trace)e;
            t.FirstVertex = new(1,2); t.SecondVertex = new(4,3); t.ThirdVertex = new(2,7); t.FourthVertex = new(6,8);
            t.Elevation = 3; t.Thickness = -2;
        }
        e.Normal = Vector3.UnitZ;
        e.Color = new AciColor(3); e.IsVisible = false;
        ((IQuadNormalProbe)e).Probe.Mode = mode;
        e.ProxyGraphics = new byte[] { 1,7,9,255 };
        return e;
    }
    private static long[] CallbackQuadState(EntityObject e)
    {
        var n = ((IQuadNormalProbe)e).StoredNormal;
        return PlanarReviewVertices(e).SelectMany(v=>new[]{v.X,v.Y})
            .Concat(new[]{PlanarReviewElevation(e),PlanarReviewThickness(e),n.X,n.Y,n.Z})
            .Select(BitConverter.DoubleToInt64Bits).ToArray();
    }
    private static void CallbackQuadCompare(EntityObject expected, EntityObject actual)
    {
        var a = PlanarReviewVertices(actual); var b = PlanarReviewVertices(expected);
        for (int i=0;i<4;i++)
        { SameDoubleBits(b[i].X,a[i].X,"Quad X"); SameDoubleBits(b[i].Y,a[i].Y,"Quad Y"); }
        SameDoubleBits(PlanarReviewElevation(expected),PlanarReviewElevation(actual),"Quad elevation");
        SameDoubleBits(PlanarReviewThickness(expected),PlanarReviewThickness(actual),"Quad thickness");
        RawLinePointBits(expected.Normal,((IQuadNormalProbe)actual).StoredNormal);
    }
    private static void RegisterQuadNormalCallbackTests()
    {
        foreach(bool trace in new[]{false,true}) foreach(bool four in new[]{false,true})
            foreach(bool owned in new[]{false,true}) for(int mode=0;mode<6;mode++)
                foreach(int scenario in new[]{0,1,5,7})
                {
                    int m=mode,k=scenario;
                    Run($"quad-normal-callback/transform/{trace}/{four}/{owned}/{m}/{k}",()=>
                    {
                        var e=CallbackQuad(trace,m); var probe=((IQuadNormalProbe)e).Probe;
                        var reference=(EntityObject)e.Clone();
                        var doc=new DxfDocument(); if(owned)doc.Entities.Add(e);
                        var owner=e.Owner; string handle=e.Handle; var color=e.Color;
                        long[] before=CallbackQuadState(e); byte[] proxy=e.ProxyGraphics!;
                        probe.Armed=true;
                        var op=PlanarReviewOperation(k);
                        reference.TransformBy(op.Matrix,op.Translation);
                        if(four)e.TransformBy(PlanarReviewMatrix4(op.Matrix,op.Translation));
                        else e.TransformBy(op.Matrix,op.Translation);
                        Equal(0,probe.Reads,"Transform invoked overridden getter"); Equal(0,probe.Writes,"Transform invoked overridden setter");
                        Equal(1,probe.Transforms,"Matrix4 lost virtual Matrix3 dispatch");
                        CallbackQuadCompare(reference,e);
                        Check(ReferenceEquals(owner,e.Owner)&&handle==e.Handle&&ReferenceEquals(color,e.Color),"Transform changed metadata identity");
                        if(k==0)
                        {Check(before.SequenceEqual(CallbackQuadState(e)),"Identity changed stored bits");Check(e.ProxyGraphics!.SequenceEqual(proxy),"Identity lost proxy");}
                        else Check(e.ProxyGraphics==null,"Changed transform retained proxy");
                    });
                }
        foreach(bool trace in new[]{false,true}) foreach(bool four in new[]{false,true}) for(int fault=0;fault<4;fault++)
        {
            int f=fault;
            Run($"quad-normal-callback/reject/{trace}/{four}/{f}",()=>
            {
                var e=CallbackQuad(trace,4); var probe=((IQuadNormalProbe)e).Probe;
                var matrix=Matrix3.Identity;var translation=Vector3.Zero;
                if(f==0)matrix=Matrix3.Scale(0);
                else if(f==1)translation=new(double.NaN,0,0);
                else if(f==2)matrix=new(1,0,1,0,1,0,0,0,1); // Oblique nonzero thickness.
                else {if(e is Solid s)s.FirstVertex=new(double.NaN,1);else ((Trace)e).FirstVertex=new(double.NaN,1);}
                e.ProxyGraphics=new byte[]{1,7,9,255};long[] before=CallbackQuadState(e);probe.Armed=true;
                Exception? error=null;
                try {if(four)e.TransformBy(PlanarReviewMatrix4(matrix,translation));else e.TransformBy(matrix,translation);}
                catch(Exception ex){error=ex;}
                Check(error is ArgumentException or NotSupportedException,"Expected geometry admission failure");
                Equal(0,probe.Reads,"Rejection invoked getter");Equal(0,probe.Writes,"Rejection invoked setter");
                Check(before.SequenceEqual(CallbackQuadState(e)),"Rejection changed geometry");
                Check(e.ProxyGraphics!.SequenceEqual(new byte[]{1,7,9,255}),"Rejection changed proxy");
            });
        }
        foreach(bool trace in new[]{false,true})
            Run($"quad-normal-callback/projective/{trace}",()=>
            {
                var e=CallbackQuad(trace,4);var p=((IQuadNormalProbe)e).Probe;p.Armed=true;
                var matrix=Matrix4.Identity;matrix.M41=.5;
                Throws<NotSupportedException>(()=>e.TransformBy(matrix));
                Equal(0,p.Transforms,"Projective Matrix4 dispatched before validation");Equal(0,p.Reads+p.Writes,"Projective input called normal accessor");
            });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})foreach(bool trace in new[]{false,true})
            Run($"quad-normal-callback/wire/{version}/{binary}/{trace}",()=>
            {
                var e=CallbackQuad(trace,4);var p=((IQuadNormalProbe)e).Probe;p.Armed=true;
                var op=PlanarReviewOperation(7);e.TransformBy(PlanarReviewMatrix4(op.Matrix,new Vector3(10,20,30)));
                Equal(0,p.Reads+p.Writes,"Wire transform invoked normal override");p.Armed=false;
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(e);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Callback quad save failed");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"quad-normal-callback-{version}-{binary}-{trace}.dxf"),stream.ToArray());
                stream.Position=0;var restored=DxfDocument.Load(stream)!;
                EntityObject actual=trace ? restored.Entities.Traces.Single() : restored.Entities.Solids.Single();
                Check(PlanarReviewState(e).SequenceEqual(PlanarReviewState(actual)),"Wire geometry changed");
                Check(actual.ProxyGraphics==null,"Stale wire proxy");
            });
    }
}
