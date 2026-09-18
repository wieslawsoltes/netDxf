// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Entities;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static Matrix2 IdentityConsumerMatrix(double[] a) => new(a[0],a[1],a[2],a[3]);
    private static double[] IdentityConsumerArray(Matrix2 m) => new[]{m.M11,m.M12,m.M21,m.M22};
    private static object IdentityConsumerCase(int index,int variant)
    {
        const int size=2;double[] input=MatrixReviewInput(size,index,variant),point=MatrixReviewPoint(size),other=MatrixReviewOther(size);
        Matrix2 m=IdentityConsumerMatrix(input),b=IdentityConsumerMatrix(other);var p=new Vector2(point[0],point[1]);
        double old=MathHelper.Epsilon;double[] vector,product,reverse,transpose,inverse;double determinant;
        try
        {
            MathHelper.Epsilon=1e-6;Check(m.IsIdentity,"Expected approximately identity input");
            vector=(m*p).ToArray();product=IdentityConsumerArray(m*b);reverse=IdentityConsumerArray(b*m);
            MatrixReviewEqual(vector,Matrix2.Multiply(m,p).ToArray(),"Matrix2 vector APIs");
            MatrixReviewEqual(product,IdentityConsumerArray(Matrix2.Multiply(m,b)),"Matrix2 product APIs");
            MatrixReviewEqual(reverse,IdentityConsumerArray(Matrix2.Multiply(b,m)),"Matrix2 reverse APIs");
            transpose=IdentityConsumerArray(m.Transpose());inverse=IdentityConsumerArray(m.Inverse());determinant=m.Determinant();
        }
        finally{MathHelper.Epsilon=old;}
        MatrixReviewEqual(MatrixReviewProduct(input,point,2,1),vector,"Matrix2 small vector effect");
        MatrixReviewEqual(MatrixReviewProduct(input,other,2,2),product,"Matrix2 left product");
        MatrixReviewEqual(MatrixReviewProduct(other,input,2,2),reverse,"Matrix2 right product");
        MatrixReviewEqual(new[]{input[0],input[2],input[1],input[3]},transpose,"Matrix2 transpose");
        var expectedInverse=new[]{1.0,0.0,0.0,1.0};bool diagonal=index==0||index==3;
        expectedInverse[index]=diagonal?1/input[index]:-input[index];
        for(int i=0;i<4;i++)Check(Math.Abs(inverse[i]-expectedInverse[i])<=Math.Abs(expectedInverse[i])*2e-15,"Matrix2 inverse");
        Equal(diagonal?input[index]:1.0,determinant,"Matrix2 determinant");MatrixReviewEqual(input,IdentityConsumerArray(m),"Matrix2 mutated input");
        return new{size,index,variant,input,point,other,vector,product,reverse,transpose,inverse,determinant};
    }
    private static void IdentityConsumerCache()
    {
        var matrix=new Matrix2(1,1e-7,0,1);double old=MathHelper.Epsilon;
        try{foreach(double epsilon in new[]{1e-12,1e-3,1e-12,1e-3}){MathHelper.Epsilon=epsilon;Equal(epsilon==1e-3,matrix.IsIdentity,"Matrix2 epsilon cache");}}
        finally{MathHelper.Epsilon=old;}
    }
    private static void IdentityConsumerExact()
    {
        var property=typeof(Matrix2).GetProperty("IsIdentityExact");Check(property!=null,"Missing Matrix2 exact query");
        Check((bool)property!.GetValue(Matrix2.Identity)!,"Exact Matrix2 rejected");
        for(int i=0;i<4;i++)Check(!(bool)property.GetValue(IdentityConsumerMatrix(MatrixReviewInput(2,i,0)))!,"Near Matrix2 accepted as exact");
        foreach(double value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            for(int i=0;i<4;i++){var a=new[]{1.0,0.0,0.0,1.0};a[i]=value;Check(!(bool)property.GetValue(IdentityConsumerMatrix(a))!,"Nonfinite identity accepted");}
    }
    private static void IdentityConsumerViewport(int index,int variant,bool four)
    {
        var input=MatrixReviewInput(2,index,variant);var m=new Matrix3(input[0],input[1],0,input[2],input[3],0,0,0,1);
        var viewport=new Viewport(new Vector2(1e12,-2e12),1024,512);var translation=new Vector3(7,-11,0);
        Vector3[] corners={new(1e12-512,-2e12-256,0),new(1e12+512,-2e12-256,0),new(1e12+512,-2e12+256,0),new(1e12-512,-2e12+256,0)};
        if(four) viewport.TransformBy(new Matrix4(m.M11,m.M12,0,7,m.M21,m.M22,0,-11,0,0,1,0,0,0,0,1));
        else viewport.TransformBy(m,translation);
        Check(viewport.ClippingBoundary is Polyline2D,"Real viewport transform skipped its boundary");
        var boundary=(Polyline2D)viewport.ClippingBoundary;Equal(4,boundary.Vertexes.Count,"Viewport corner inventory");Check(boundary.IsClosed,"Viewport boundary opened");
        for(int i=0;i<4;i++)
        {
            var expected=m*corners[i]+translation;var actual=boundary.Vertexes[i].Position;
            Equal(expected.X,actual.X,"Viewport transformed corner X");Equal(expected.Y,actual.Y,"Viewport transformed corner Y");
        }
    }
    private static void RegisterIdentityConsumerReviewTests()
    {
        for(int index=0;index<4;index++)for(int variant=0;variant<3;variant++)
        {int i=index,v=variant;Run($"identity-consumers/matrix2/{i}/{v}",()=>IdentityConsumerCase(i,v));
         foreach(bool four in new[]{false,true})Run($"identity-consumers/viewport/{i}/{v}/{four}",()=>IdentityConsumerViewport(i,v,four));}
        Run("identity-consumers/cache",IdentityConsumerCache);Run("identity-consumers/exact",IdentityConsumerExact);
        Run("identity-consumers/signed",()=>{var m=Matrix2.Identity;m.M12=-0.0;var v=new Vector2(-0.0,double.NaN);Check(v.ToArray().Select(BitConverter.DoubleToInt64Bits).SequenceEqual((m*v).ToArray().Select(BitConverter.DoubleToInt64Bits)),"Exact identity changed vector bits");});
        Run("identity-consumers/viewport-identity",()=>{var v=new Viewport(new Vector2(1e12,-2e12),1024,512);v.TransformBy(Matrix3.Identity,new Vector3(7,-11,0));Check(v.ClippingBoundary==null,"Exact translation added a boundary");Equal(1e12+7,v.Center.X,"Translation X");Equal(-2e12-11,v.Center.Y,"Translation Y");});
        Run("identity-consumers/numerics",()=>{var rows=new List<object>();for(int i=0;i<4;i++)for(int v=0;v<3;v++)rows.Add(IdentityConsumerCase(i,v));File.WriteAllText(Path.Combine(ArtifactDirectory,"matrix2-identity-numerics.json"),JsonSerializer.Serialize(rows));});
    }
}
