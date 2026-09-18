// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static double[] Vector4ReviewComponents(Vector4 v) => new[] {v.X,v.Y,v.Z,v.W};
    private static long[] Vector4ReviewBits(Vector4 v) => Vector4ReviewComponents(v).Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static double[] Vector4ReviewInput(int index, int side) => Enumerable.Range(0,4)
        .Select(c => (double)(((index+1)*(17+c*12)+(side+1)*(31+c*8))%97-48)).ToArray();
    private static Vector4 Vector4ReviewVector(double[] a) => new(a[0],a[1],a[2],a[3]);
    private static void Vector4ReviewArray(int index)
    {
        double special = index switch
        {
            0 => 0.0, 1 => -0.0, 2 => double.Epsilon, 3 => -double.Epsilon,
            4 => double.MaxValue, 5 => -double.MaxValue, 6 => double.PositiveInfinity,
            7 => double.NegativeInfinity, 8 => BitConverter.Int64BitsToDouble(0x7ff8000000000042),
            _ => BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000021))
        };
        for(int component=0;component<4;component++)
        {
            double[] values={1.0,-2.0,3.0,-4.0};values[component]=special;
            var v=Vector4ReviewVector(values);long[] before=Vector4ReviewBits(v);
            double[] first=v.ToArray(),second=v.ToArray();
            Equal(4,first.Length,"Vector4 exported component count");
            Check(first.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(before),"Vector4 export lost component bits");
            Check(Vector4ReviewBits(new Vector4(first)).SequenceEqual(before),"Array constructor round trip changed bits");
            Check(!ReferenceEquals(first,second),"Array exports share storage");
            first[component]=19;
            Check(Vector4ReviewBits(v).SequenceEqual(before) && second.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(before),"Export mutation changed vector or another export");
        }
    }
    private static object Vector4ReviewDistance(int index)
    {
        double[] a=Vector4ReviewInput(index,0),b=Vector4ReviewInput(index,1);
        var u=Vector4ReviewVector(a);var v=Vector4ReviewVector(b);long[] beforeU=Vector4ReviewBits(u),beforeV=Vector4ReviewBits(v);
        long exact=0;for(int c=0;c<4;c++){long delta=(long)a[c]-(long)b[c];exact+=delta*delta;}
        double squared=Vector4.SquareDistance(u,v),distance=Vector4.Distance(u,v);
        Equal((double)exact,squared,"Four-dimensional square distance");
        Equal(squared,Vector4.SquareDistance(v,u),"Distance symmetry");
        Equal(Math.Sqrt(exact),distance,"Distance square root");
        Equal(0.0,Vector4.SquareDistance(u,u),"Self distance");
        var translation=new Vector4(7,-11,19,23);
        Equal(squared,Vector4.SquareDistance(u+translation,v+translation),"Translation invariance");
        Check(Vector4ReviewBits(u).SequenceEqual(beforeU) && Vector4ReviewBits(v).SequenceEqual(beforeV),"Distance mutated inputs");
        return new{index,a,b,squared,distance};
    }
    private static void Vector4ReviewWOnly(int index)
    {
        double z=index-16,w=index%5-2;Vector4 a=new Vector4(1,2,z,w),b=new Vector4(1,2,z,w+3);
        Equal(9.0,Vector4.SquareDistance(a,b),"W-only separation");Equal(3.0,Vector4.Distance(a,b),"W-only length");
        Equal(9.0,Vector4.SquareDistance(b,a),"W-only reverse separation");
    }
    private static void RegisterVector4ComponentReviewTests()
    {
        for(int i=0;i<10;i++){int n=i;Run($"vector4-components/export/{n}",()=>Vector4ReviewArray(n));}
        for(int i=0;i<128;i++){int n=i;Run($"vector4-components/distance/{n}",()=>Vector4ReviewDistance(n));}
        for(int i=0;i<32;i++){int n=i;Run($"vector4-components/w-only/{n}",()=>Vector4ReviewWOnly(n));}
        Run("vector4-components/numerics",()=>
        {
            var rows=new List<object>();for(int i=0;i<512;i++)rows.Add(Vector4ReviewDistance(i));
            File.WriteAllText(Path.Combine(ArtifactDirectory,"vector4-components-numerics.json"),JsonSerializer.Serialize(rows));
        });
    }
}
