// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static double[] MatrixReviewArray(Vector3 v) => new[] {v.X,v.Y,v.Z};
    private static double[] MatrixReviewArray(Vector4 v) => new[] {v.X,v.Y,v.Z,v.W};
    private static double[] MatrixReviewArray(Matrix3 m) => new[] { m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33 };
    private static double[] MatrixReviewArray(Matrix4 m) => new[] { m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 };
    private static double[] MatrixReviewPoint(int size) => Enumerable.Range(0,size).Select(i => Math.ScaleB(i%2==0 ? i+1 : -i-1,40)).ToArray();
    private static double[] MatrixReviewProduct(double[] a,double[] b,int size,int columns)
    {
        var result = new double[size*columns];
        for (int r=0;r<size;r++) for(int c=0;c<columns;c++)
        {
            double sum=a[r*size]*b[c];
            for(int k=1;k<size;k++) sum+=a[r*size+k]*b[k*columns+c];
            result[r*columns+c]=sum;
        }
        return result;
    }
    private static void MatrixReviewEqual(double[] expected,double[] actual,string message)
    {
        Check(expected.Length==actual.Length,message+" length");
        for(int i=0;i<expected.Length;i++)
            Check(expected[i]==actual[i],$"{message} [{i}]: {expected[i]:R} != {actual[i]:R}");
    }
    private static double[] MatrixReviewOther(int size) => Enumerable.Range(0,size*size).Select(i => (double)(i%7-3)).ToArray();
    private static Matrix3 MatrixReview3(double[] a) => new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8]);
    private static Matrix4 MatrixReview4(double[] a) => new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);
    private static double[] MatrixReviewInput(int size,int index,int variant)
    {
        var a = new double[size*size];for(int i=0;i<size;i++) a[i*size+i]=1;
        double delta=variant switch {0=>Math.ScaleB(1.0,-45),1=>-Math.ScaleB(1.0,-45),_=>1e-13};
        a[index]+=delta;return a;
    }
    private static object MatrixReviewCase(int size,int index,int variant)
    {
        double[] input=MatrixReviewInput(size,index,variant),other=MatrixReviewOther(size),point=MatrixReviewPoint(size);
        double[] vector,product,reverse,transpose,inverse;double determinant;
        double saved=MathHelper.Epsilon;
        try
        {
            MathHelper.Epsilon=1e-6;
            if(size==3)
            {
                var m=MatrixReview3(input);var b=MatrixReview3(other);var p=new Vector3(point[0],point[1],point[2]);
                Check(m.IsIdentity,"Regression matrix must be approximately identity");
                vector=MatrixReviewArray(m*p);product=MatrixReviewArray(m*b);reverse=MatrixReviewArray(b*m);
                MatrixReviewEqual(vector,MatrixReviewArray(Matrix3.Multiply(m,p)),"Matrix3 vector APIs");
                MatrixReviewEqual(product,MatrixReviewArray(Matrix3.Multiply(m,b)),"Matrix3 product APIs");
                MatrixReviewEqual(reverse,MatrixReviewArray(Matrix3.Multiply(b,m)),"Matrix3 reverse APIs");
                transpose=MatrixReviewArray(m.Transpose());inverse=MatrixReviewArray(m.Inverse());determinant=m.Determinant();
                MatrixReviewEqual(input,MatrixReviewArray(m),"Matrix3 input mutated");
            }
            else
            {
                var m=MatrixReview4(input);var b=MatrixReview4(other);var p=new Vector4(point[0],point[1],point[2],point[3]);
                Check(m.IsIdentity,"Regression matrix must be approximately identity");
                vector=MatrixReviewArray(m*p);product=MatrixReviewArray(m*b);reverse=MatrixReviewArray(b*m);
                MatrixReviewEqual(vector,MatrixReviewArray(Matrix4.Multiply(m,p)),"Matrix4 vector APIs");
                MatrixReviewEqual(product,MatrixReviewArray(Matrix4.Multiply(m,b)),"Matrix4 product APIs");
                MatrixReviewEqual(reverse,MatrixReviewArray(Matrix4.Multiply(b,m)),"Matrix4 reverse APIs");
                transpose=MatrixReviewArray(m.Transpose());inverse=MatrixReviewArray(m.Inverse());determinant=m.Determinant();
                MatrixReviewEqual(input,MatrixReviewArray(m),"Matrix4 input mutated");
            }
        }
        finally {MathHelper.Epsilon=saved;}
        MatrixReviewEqual(MatrixReviewProduct(input,point,size,1),vector,"Small vector effect lost");
        MatrixReviewEqual(MatrixReviewProduct(input,other,size,size),product,"Left small matrix effect lost");
        MatrixReviewEqual(MatrixReviewProduct(other,input,size,size),reverse,"Right small matrix effect lost");
        var expectedTranspose=new double[size*size];var expectedInverse=new double[size*size];
        for(int r=0;r<size;r++)for(int c=0;c<size;c++){expectedTranspose[r*size+c]=input[c*size+r];expectedInverse[r*size+c]=r==c?1:0;}
        int row=index/size,column=index%size;
        expectedInverse[index]=row==column?1/input[index]:-input[index];
        MatrixReviewEqual(expectedTranspose,transpose,"Transpose snapped to identity");
        for(int i=0;i<inverse.Length;i++)
        {
            double tolerance=Math.Abs(expectedInverse[i])*2e-15;
            Check(double.IsFinite(inverse[i]) && Math.Abs(inverse[i]-expectedInverse[i])<=tolerance,"Inverse snapped to identity");
        }
        Equal(row==column?input[index]:1.0,determinant,"Determinant snapped to identity");
        return new {size,index,variant,input,point,other,vector,product,reverse,transpose,inverse,determinant};
    }
    private static void MatrixReviewCache(int size)
    {
        double old=MathHelper.Epsilon;
        try
        {
            var a=MatrixReview3(new[]{1.0,1e-7,0.0,0.0,1.0,0.0,0.0,0.0,1.0});
            var b=Matrix4.Identity;b.M14=1e-7;
            foreach(double epsilon in new[]{1e-12,1e-3,1e-12,1e-3})
            {
                MathHelper.Epsilon=epsilon;bool actual=size==3?a.IsIdentity:b.IsIdentity;
                Equal(epsilon==1e-3,actual,"Cached identity ignored the current epsilon");
            }
        }
        finally{MathHelper.Epsilon=old;}
    }
    private static void MatrixReviewExactApi(int size)
    {
        var type=size==3?typeof(Matrix3):typeof(Matrix4);var property=type.GetProperty("IsIdentityExact");
        Check(property!=null,"Missing exact identity query");
        object exact=size==3?Matrix3.Identity:Matrix4.Identity;
        Check((bool)property!.GetValue(exact)!,"Exact identity rejected");
        for(int i=0;i<size*size;i++)
        {
            double[] a=MatrixReviewInput(size,i,0);object item=size==3?MatrixReview3(a):MatrixReview4(a);
            Check(!(bool)property.GetValue(item)!,"Near identity accepted as exact");
        }
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            for(int i=0;i<size*size;i++)
            {
                double[] a=MatrixReviewInput(size,0,0);a[0]=1;a[i]=bad;
                object item=size==3?MatrixReview3(a):MatrixReview4(a);
                Check(!(bool)property.GetValue(item)!,"Nonfinite matrix accepted as identity");
            }
    }
    private static void MatrixReviewSignedIdentity(int size)
    {
        double old=MathHelper.Epsilon;
        try
        {
            MathHelper.Epsilon=100;
            if(size==3)
            {
                var m=Matrix3.Identity;m.M12=-0.0;var p=new Vector3(-0.0,double.NaN,double.PositiveInfinity);
                Check(DirectionBits(p).SequenceEqual(DirectionBits(m*p)),"Exact identity changed vector bits");
                Check(DirectionBits(p).SequenceEqual(DirectionBits(Matrix3.Multiply(m,p))),"Exact static identity changed vector bits");
            }
            else
            {
                var m=Matrix4.Identity;m.M14=-0.0;var p=new Vector4(-0.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity);
                Check(MatrixReviewArray(p).Select(BitConverter.DoubleToInt64Bits).SequenceEqual(MatrixReviewArray(m*p).Select(BitConverter.DoubleToInt64Bits)),"Exact Matrix4 changed vector bits");
            }
        }
        finally{MathHelper.Epsilon=old;}
    }
    private static void RegisterMatrixIdentityReviewTests()
    {
        foreach(int size in new[]{3,4})
        {
            for(int index=0;index<size*size;index++) for(int variant=0;variant<3;variant++)
            {int at=index,kind=variant;Run($"matrix-identity/{size}/{at}/{kind}",()=>MatrixReviewCase(size,at,kind));}
            Run($"matrix-identity/cache/{size}",()=>MatrixReviewCache(size));
            Run($"matrix-identity/exact-api/{size}",()=>MatrixReviewExactApi(size));
            Run($"matrix-identity/signed/{size}",()=>MatrixReviewSignedIdentity(size));
        }
        Run("matrix-identity/numerics",()=>
        {
            var rows=new List<object>();foreach(int size in new[]{3,4})for(int index=0;index<size*size;index++)for(int variant=0;variant<3;variant++)rows.Add(MatrixReviewCase(size,index,variant));
            File.WriteAllText(Path.Combine(ArtifactDirectory,"matrix-identity-numerics.json"),JsonSerializer.Serialize(rows));
        });
    }
}
