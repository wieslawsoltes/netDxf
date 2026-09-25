// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;
using Attribute = netDxf.Entities.Attribute;

namespace NetDxf.Qualification
{
    internal static class InsertGeometryCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly Action Test;
            internal Case(string id, Action test) { Id = id; Test = test; }
        }
        private static readonly byte[] Proxy = Enumerable.Range(0, 300).Select(i => (byte)(i * 31)).ToArray();
        private static readonly DxfVersion[] Versions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
            DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
        private static Vector3 Normal(int plane) { return plane == 0 ? Vector3.UnitZ : plane == 1 ? -Vector3.UnitZ : new Vector3(0, 3, 4); }
        private static Vector3 Shift(int map) { return map == 0 ? Vector3.Zero : new Vector3(11, -13, 17); }
        private static Matrix3 Frame(int plane)
        { return MathHelper.ArbitraryAxis(Vector3.Normalize(Normal(plane))) * Matrix3.RotationZ(37 * MathHelper.DegToRad); }
        private static Matrix3 Map(int plane, int map)
        {
            switch (map)
            {
                case 0: case 1: return Matrix3.Identity;
                case 2: return new Matrix3(0, 1, 0, 1, 0, 0, 0, 0, 1);
                case 3: return Matrix3.Scale(-2);
                case 4: return Frame(plane) * Matrix3.Scale(2, 3, 4) * Frame(plane).Transpose();
                case 5: return Matrix3.Scale(1e-14);
                case 6: return Matrix3.RotationX(0.7);
                default: throw new ArgumentOutOfRangeException(nameof(map));
            }
        }
        private static Matrix4 Four(Matrix3 m, Vector3 t)
        { return new Matrix4(m.M11,m.M12,m.M13,t.X,m.M21,m.M22,m.M23,t.Y,m.M31,m.M32,m.M33,t.Z,0,0,0,1); }
        private static void Apply(Insert item, Matrix3 m, Vector3 t, bool four)
        { if (four) item.TransformBy(Four(m,t)); else item.TransformBy(m,t); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        private static void Same(Vector3 a, Vector3 b, string label)
        { Check(Bits(a.X)==Bits(b.X) && Bits(a.Y)==Bits(b.Y) && Bits(a.Z)==Bits(b.Z), label); }
        private static void Near(Vector3 a, Vector3 b, string label)
        {
            double magnitude = Math.Max(Math.Max(Math.Abs(a.X),Math.Abs(a.Y)),Math.Abs(a.Z));
            for (int i=0;i<3;i++)
                Check(!double.IsNaN(b[i]) && !double.IsInfinity(b[i])
                    && Math.Abs(a[i]-b[i]) <= Math.Max(1e-28, 2e-10*magnitude), label);
        }
        private static bool Cache(byte[]? a, byte[]? b)
        { return a==null ? b==null : b!=null && a.SequenceEqual(b); }
        private static Insert Subject(int plane, bool multiple, int signs = 2)
        {
            var block = new Block("IG_COMPONENT") { Origin = new Vector3(1,-2,3) };
            block.Entities.Add(new Line(new Vector3(2,3,4), new Vector3(5,-1,7)));
            block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Position = new Vector3(2,3,0), Height = 2, Value = "KEEP" });
            var item = new Insert(block,new Vector3(5,-3,7))
            {
                Normal = Normal(plane), Rotation = 37,
                Scale = new Vector3((signs&1)==0?2:-2,(signs&2)==0?3:-3,(signs&4)==0?4:-4),
                RowCount = (short)(multiple?2:1), ColumnCount = (short)(multiple?3:1),
                ColumnSpacing = 7.5, RowSpacing = -4.25
            };
            var data = new XData(new ApplicationRegistry("IG_KEEP"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.String,"unchanged")); item.XData.Add(data);
            return item;
        }
        private static Vector3[] Geometry(Insert item)
        {
            Matrix3 m=item.GetTransformation(DrawingUnits.Unitless);
            Matrix3 frame=MathHelper.ArbitraryAxis(item.Normal)*Matrix3.RotationZ(item.Rotation*MathHelper.DegToRad);
            return new[] { item.Position,m*Vector3.UnitX,m*Vector3.UnitY,m*Vector3.UnitZ,
                frame*new Vector3(item.ColumnSpacing,0,0),frame*new Vector3(0,item.RowSpacing,0) };
        }
        private static void Compare(Vector3[] source, Insert actual, Matrix3 map, Vector3 shift)
        {
            var result=Geometry(actual);
            for(int k=0;k<source.Length;k++) Near(map*source[k]+(k==0?shift:Vector3.Zero),result[k],"INSERT mapped basis/spacing " + k);
        }
        private static long[] State(Insert item)
        {
            return new[] { item.Position.X,item.Position.Y,item.Position.Z,item.Normal.X,item.Normal.Y,item.Normal.Z,
                item.Scale.X,item.Scale.Y,item.Scale.Z,item.Rotation,item.ColumnSpacing,item.RowSpacing,(double)item.ColumnCount,item.RowCount }
                .Concat(item.Attributes.SelectMany(a=>new[] {a.Position.X,a.Position.Y,a.Position.Z,a.Normal.X,a.Normal.Y,a.Normal.Z,
                    a.Height,a.Width,a.WidthFactor,a.Rotation,a.ObliqueAngle,(double)a.Alignment,a.IsBackward?1.0:0.0,a.IsUpsideDown?1.0:0.0}))
                .Select(Bits).ToArray();
        }
        private static void Refused(Insert item, Action operation)
        {
            item.ProxyGraphics=Proxy;
            foreach(var att in item.Attributes) att.ProxyGraphics=Proxy;
            var state=State(item);var attributes=item.Attributes.ToArray();var block=item.Block;
            var styles=attributes.Select(a=>a.Style).ToArray();var owner=item.Owner;string handle=item.Handle;
            bool rejected=false;
            try { operation(); } catch(Exception e) when(e is ArgumentException || e is NotSupportedException) { rejected=true; }
            Check(rejected,"Unrepresentable/invalid INSERT operation accepted");
            Check(state.SequenceEqual(State(item)) && Cache(Proxy,item.ProxyGraphics),"Refused INSERT mutated geometry/cache");
            Check(ReferenceEquals(block,item.Block) && ReferenceEquals(owner,item.Owner) && handle==item.Handle,"Refused INSERT identity");
            for(int i=0;i<attributes.Length;i++) Check(ReferenceEquals(attributes[i],item.Attributes[i]) && ReferenceEquals(styles[i],item.Attributes[i].Style)
                && Cache(Proxy,item.Attributes[i].ProxyGraphics),"Refused INSERT changed attribute identity/resource/cache");
        }
        private static void Api(int plane,int map,bool multiple,bool four,int signs)
        {
            var item=Subject(plane,multiple,signs); var doc=new DxfDocument();doc.Entities.Add(item);
            var original=Geometry(item);var state=State(item);var attributes=item.Attributes.ToArray();
            var positions=attributes.Select(a=>a.Position).ToArray();var definition=attributes[0].Definition;var style=attributes[0].Style;
            var block=item.Block;string handle=item.Handle;item.ProxyGraphics=Proxy;
            foreach(var att in attributes)att.ProxyGraphics=Proxy;
            Apply(item,Map(plane,map),Shift(map),four);Compare(original,item,Map(plane,map),Shift(map));
            if(map==0) Check(state.SequenceEqual(State(item)),"Identity changed stored INSERT/ATTRIB bits");
            Check(Cache(map==0?Proxy:null,item.ProxyGraphics),"INSERT graphics invalidation");
            for(int i=0;i<attributes.Length;i++)
            {
                Check(ReferenceEquals(attributes[i],item.Attributes[i]),"Attribute replacement");
                Near(Map(plane,map)*positions[i]+Shift(map),item.Attributes[i].Position,"Attribute position");
                Check(Cache(map==0?Proxy:null,item.Attributes[i].ProxyGraphics),"Attribute graphics invalidation");
            }
            Check(ReferenceEquals(definition,item.Attributes[0].Definition)&&ReferenceEquals(style,item.Attributes[0].Style)
                &&ReferenceEquals(block,item.Block)&&handle==item.Handle,"INSERT resources/identity");
            Check(doc.Objects.Validate().Count==0,"INSERT graph");
            var copy=(Insert)item.Clone();Compare(original,copy,Map(plane,map),Shift(map));
            Check(!ReferenceEquals(copy.Block,item.Block)&&!ReferenceEquals(copy.Attributes[0],item.Attributes[0]),"Clone aliases");
            copy.ColumnCount=3;copy.RowCount=2;
            var originalFrame=Frame(plane);
            Near(Map(plane,map)*(new Vector3(5,-3,7)+originalFrame*new Vector3(15,-4.25,0))+Shift(map),copy.GetGridPosition(1,2),"Dormant grid activation");
        }
        internal static IEnumerable<Case> All(string? directory)
        {
            for(int p=0;p<3;p++)for(int m=0;m<7;m++)foreach(bool multiple in new[]{false,true})foreach(bool four in new[]{false,true})
            {
                int plane=p,map=m;
                yield return new Case($"api/{p}/{m}/{multiple}/{four}",()=>Api(plane,map,multiple,four,2));
            }
            for(int s=0;s<8;s++)for(int p=0;p<3;p++)
            {
                int signs=s,plane=p;
                yield return new Case($"signs/{p}/{s}",()=>Api(plane,2,false,false,signs));
            }
            foreach(bool multiple in new[]{false,true})
            {
                for(int r=0;r<4;r++)for(int c=0;c<4;c++)foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                {
                    int row=r,col=c;double value=bad;
                    yield return new Case($"nonfinite/{multiple}/{r}/{c}/{Bits(bad):x16}",()=>{
                        var item=Subject(0,multiple);var m=Matrix4.Identity;m[row,col]=value;Refused(item,()=>item.TransformBy(m)); });
                }
                for(int col=0;col<4;col++)
                {
                    int c=col;
                    yield return new Case($"projective/{multiple}/{col}",()=>{
                        var item=Subject(0,multiple);var m=Matrix4.Identity;m[3,c]+=1e-7;Refused(item,()=>item.TransformBy(m)); });
                }
                for(int p=0;p<3;p++)foreach(bool four in new[]{false,true})
                {
                    int plane=p;
                    yield return new Case($"shear/{multiple}/{p}/{four}",()=>{
                        var item=Subject(plane,multiple);var f=Frame(plane);var shear=f*new Matrix3(1,.5,0,0,1,0,0,0,1)*f.Transpose();
                        Refused(item,()=>Apply(item,shear,Vector3.Zero,four)); });
                    yield return new Case($"collapse/{multiple}/{p}/{four}",()=>{
                        var item=Subject(plane,multiple);Refused(item,()=>Apply(item,Matrix3.Scale(0),Vector3.Zero,four)); });
                }
                yield return new Case("attribute-atomic/"+multiple,()=>{
                    var item=Subject(0,multiple);item.Block.AttributeDefinitions.Add(new AttributeDefinition("SECOND"));item.Sync();
                    item.Attributes[1].Position=new Vector3(double.MaxValue,0,0);
                    Refused(item,()=>item.TransformBy(Matrix3.Scale(2),Vector3.Zero)); });
                yield return new Case("detached-block/"+multiple,()=>{
                    var item=Subject(0,multiple);var container=new Block("Detached");container.Entities.Add(item);
                    item.TransformBy(Matrix3.Scale(2),Vector3.Zero);Near(new Vector3(12,4,8),item.Attributes[0].Position,"Detached attribute"); });
            }
            yield return new Case("setters",Setters);
            yield return new Case("invalid-direct-edits",()=>{
                var item=Subject(0,false);
                foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                {
                    Refused(item,()=>item.Rotation=bad);
                    for(int k=0;k<3;k++){var position=Vector3.Zero;position[k]=bad;Refused(item,()=>item.Position=position);}
                    Refused(item,()=>item.ColumnSpacing=bad);Refused(item,()=>item.RowSpacing=bad);
                }
                Refused(item,()=>item.RowCount=0);Refused(item,()=>item.ColumnCount=0);
            });
            yield return new Case("translation-bits-and-cancellation",()=>{
                var item=Subject(2,false);var scale=item.Scale;var normal=item.Normal;double rotation=item.Rotation;
                item.TransformBy(Matrix3.Identity,new Vector3(1,2,3));
                Same(scale,item.Scale,"Translated scale");Same(normal,item.Normal,"Translated normal");
                Check(Bits(rotation)==Bits(item.Rotation),"Translated rotation");
                var extreme=new Insert(new Block("Extreme"),new Vector3(double.MaxValue,0,0));
                extreme.TransformBy(Matrix3.Scale(2),new Vector3(-double.MaxValue,0,0));
                Same(new Vector3(double.MaxValue,0,0),extreme.Position,"Finite cancellation");
                Refused(extreme,()=>extreme.TransformBy(Matrix3.Scale(2),Vector3.Zero));
            });
            yield return new Case("finite-scalar-admission",()=>{
                var item=Subject(0,false);
                foreach(double value in new[]{double.Epsilon,-double.Epsilon,1e-20,-1e-20,double.MaxValue,-double.MaxValue})
                { item.Scale=new Vector3(value);Same(new Vector3(value),item.Scale,"Stored finite scale"); }
                foreach(double value in new[]{0.0,-0.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                for(int k=0;k<3;k++)
                { var scale=Vector3.UnitX+Vector3.UnitY+Vector3.UnitZ;scale[k]=value;Refused(item,()=>item.Scale=scale); }
            });
            yield return new Case("virtual-dispatch",()=>{
                var item=new NormalTrap(new Block("Trap"));item.Armed=true;
                item.TransformBy(Matrix3.RotationX(.7),Vector3.Zero);Check(item.Writes==0,"Virtual Normal callback reached");
                var dispatch=new DispatchTrap(new Block("Dispatch"));var m=Matrix4.Identity;m.M41=.25;
                Refused(dispatch,()=>dispatch.TransformBy(m));Check(dispatch.Calls==0,"Projective input reached virtual Matrix3");
            });
            foreach(var version in Versions)foreach(bool binary in new[]{false,true})for(int place=0;place<4;place++)
            {
                int placement=place;
                yield return new Case($"wire/{version}/{binary}/{place}",()=>Wire(version,binary,placement,directory));
                yield return new Case($"scalars/{version}/{binary}/{place}",()=>ScalarWire(version,binary,placement,directory));
            }
        }
        private sealed class NormalTrap : Insert
        {
            internal bool Armed;internal int Writes;
            internal NormalTrap(Block block):base(block){}
            public override Vector3 Normal { get { return base.Normal; } set { if(Armed){Writes++;throw new InvalidOperationException("callback");}base.Normal=value; } }
        }
        private sealed class DispatchTrap : Insert
        {
            internal int Calls;internal DispatchTrap(Block block):base(block){}
            public override void TransformBy(Matrix3 m,Vector3 t){Calls++;base.TransformBy(m,t);}
        }
        private static void Setters()
        {
            Action<Insert>[] same={i=>i.Position=i.Position,i=>i.Scale=i.Scale,i=>i.Rotation=i.Rotation,
                i=>i.ColumnCount=i.ColumnCount,i=>i.RowCount=i.RowCount,i=>i.ColumnSpacing=i.ColumnSpacing,i=>i.RowSpacing=i.RowSpacing};
            Action<Insert>[] changed={i=>i.Position+=Vector3.UnitX,i=>i.Scale*=2,i=>i.Rotation+=5,
                i=>i.ColumnCount++,i=>i.RowCount++,i=>i.ColumnSpacing+=2,i=>i.RowSpacing+=2};
            foreach(byte[]? cache in new byte[]?[]{null,new byte[0],Proxy})for(int k=0;k<same.Length;k++)
            {
                var item=Subject(0,false);item.ProxyGraphics=cache;same[k](item);Check(Cache(cache,item.ProxyGraphics),"Setter no-op cache");
                changed[k](item);Check(item.ProxyGraphics==null,"Setter retained stale INSERT graphics");
            }
        }
        private static void Place(DxfDocument doc,List<Insert> items,int placement)
        {
            if(placement==0)foreach(var item in items)doc.Entities.Add(item);
            else if(placement==1)
            { var layout=new Layout("IG_PAPER");doc.Layouts.Add(layout);foreach(var item in items)layout.AssociatedBlock.Entities.Add(item); }
            else
            {
                var block=new Block("IG_CONTAINER");foreach(var item in items)block.Entities.Add(item);
                if(placement==2)doc.Entities.Add(new Insert(block));else doc.Blocks.Add(block);
            }
            doc.Entities.Add(new Line(new Vector3(101,102,103),new Vector3(104,105,106)));
        }
        private static byte[] Save(DxfDocument doc,bool binary)
        { using(var s=new MemoryStream()){Check(doc.Save(s,binary)&&s.CanWrite,"Save stream");return s.ToArray();} }
        private static DxfDocument Load(byte[] bytes)
        {
            var before=(byte[])bytes.Clone();using(var s=new MemoryStream(bytes))
            { var doc=DxfDocument.Load(s)??throw new InvalidOperationException("Load INSERT drawing");Check(s.CanRead&&bytes.SequenceEqual(before),"Input bytes/stream changed");return doc; }
        }
        private static void Store(string? directory,string prefix,string stage,byte[] bytes)
        { if(directory!=null)File.WriteAllBytes(Path.Combine(directory,prefix+"-"+stage+".dxf"),bytes); }
        private static void Wire(DxfVersion version,bool binary,int placement,string? directory)
        {
            var doc=new DxfDocument(version);var items=new List<Insert>();
            for(int p=0;p<3;p++)for(int m=0;m<7;m++)foreach(bool multiple in new[]{false,true})
            {
                var item=Subject(p,multiple);item.Block.Name=$"IG_{p}_{m}_{(multiple?1:0)}";items.Add(item);
            }
            Place(doc,items,placement);foreach(var item in items){item.ProxyGraphics=Proxy;item.Attributes[0].ProxyGraphics=Proxy;}
            string prefix=$"insert-geometry-{version}-{(binary?"binary":"text")}-{placement}";
            byte[] source=Save(doc,binary);Store(directory,prefix,"source",source);var loaded=Load(source);
            int index=0;
            for(int p=0;p<3;p++)for(int m=0;m<7;m++)foreach(bool multiple in new[]{false,true})
            {
                var item=(Insert)loaded.GetObjectByHandle(items[index++].Handle);Check(Cache(Proxy,item.ProxyGraphics),"Hydrated INSERT cache");
                Check(Cache(Proxy,item.Attributes[0].ProxyGraphics),"Hydrated ATTRIB cache");
                var seed=Geometry(Subject(p,multiple));item.TransformBy(Map(p,m),Shift(m));Compare(seed,item,Map(p,m),Shift(m));
            }
            byte[] output=Save(loaded,binary);Store(directory,prefix,"output",output);
            var reloaded=Load(output);VerifyWire(items,reloaded);
            byte[] resave=Save(reloaded,!binary);Store(directory,prefix,"resave",resave);VerifyWire(items,Load(resave));
        }
        private static void VerifyWire(List<Insert> seeds,DxfDocument doc)
        {
            int index=0;
            for(int p=0;p<3;p++)for(int m=0;m<7;m++)foreach(bool multiple in new[]{false,true})
            {
                var item=(Insert)doc.GetObjectByHandle(seeds[index++].Handle);Compare(Geometry(Subject(p,multiple)),item,Map(p,m),Shift(m));
                Check(Cache(m==0?Proxy:null,item.ProxyGraphics)&&Cache(m==0?Proxy:null,item.Attributes[0].ProxyGraphics),"Wire graphics");
                Check(item.Attributes[0].Value=="KEEP"&&ReferenceEquals(item.Attributes[0].Owner,item),"Wire attributes");
            }
            var following=doc.Entities.Lines.Single();
            Same(new Vector3(101,102,103),following.StartPoint,"Following LINE start");
            Same(new Vector3(104,105,106),following.EndPoint,"Following LINE end");
            Check(doc.Objects.Validate().Count==0,"Wire graph");
        }
        private static readonly Vector3[] Scalars={new Vector3(2,-3,4),new Vector3(1e-20,-2e-20,3e-20),
            new Vector3(double.Epsilon,-double.Epsilon,double.Epsilon),new Vector3(0.0,-0.0,0.0),
            new Vector3(double.MaxValue,-2.2250738585072014e-308,1),new Vector3(1),new Vector3(1,2,1)};
        private static void ScalarWire(DxfVersion version,bool binary,int placement,string? directory)
        {
            var doc=new DxfDocument(version);var items=new List<Insert>();
            for(int i=0;i<Scalars.Length;i++)
            {
                var item=Subject(0,(i&1)!=0);item.Rotation=0;item.Normal=Vector3.UnitZ;item.Block.Name="IG_SCALAR_"+i;items.Add(item);
            }
            Place(doc,items,placement);foreach(var item in items)item.ProxyGraphics=Proxy;
            string[] lines=Encoding.UTF8.GetString(Save(doc,false)).Replace("\r\n","\n").Split('\n');
            var tags=new List<string>();int index=-1;bool insert=false;
            for(int i=0;i+1<lines.Length;i+=2)
            {
                int code=int.Parse(lines[i],CultureInfo.InvariantCulture);string value=lines[i+1];
                if(code==0){insert=value=="INSERT";index=-1;}
                if(insert&&code==2&&value.StartsWith("IG_SCALAR_",StringComparison.Ordinal))index=int.Parse(value.Substring(10),CultureInfo.InvariantCulture);
                if(index>=0&&code>=41&&code<=43)
                {
                    if(index==5||(index==6&&code!=42))continue;
                    double scalar=Scalars[index][code-41];value=Bits(scalar)==long.MinValue?"-0.0":scalar.ToString("R",CultureInfo.InvariantCulture);
                }
                tags.Add(lines[i]);tags.Add(value);
            }
            byte[] source=Encoding.UTF8.GetBytes(string.Join("\n",tags)+"\n");
            string prefix=$"insert-scalars-{version}-{(binary?"binary":"text")}-{placement}";Store(directory,prefix,"source",source);
            var loaded=Load(source);VerifyScalars(items,loaded);
            foreach(var seed in items)
            {
                var item=(Insert)loaded.GetObjectByHandle(seed.Handle);Same(item.Scale,((Insert)item.Clone()).Scale,"Clone scalar bits");
                var scale=item.Scale;item.TransformBy(Matrix3.Identity,Vector3.Zero);Same(scale,item.Scale,"Identity scalar bits");
                item.TransformBy(Matrix3.Identity,new Vector3(1,2,3));Same(scale,item.Scale,"Translation scalar bits");
            }
            byte[] output=Save(loaded,binary);Store(directory,prefix,"output",output);var reloaded=Load(output);VerifyScalars(items,reloaded);
            byte[] resave=Save(reloaded,!binary);Store(directory,prefix,"resave",resave);VerifyScalars(items,Load(resave));
            var collapsed=(Insert)reloaded.GetObjectByHandle(items[3].Handle);Refused(collapsed,()=>collapsed.TransformBy(Matrix3.Scale(2),Vector3.Zero));
        }
        private static void VerifyScalars(List<Insert> seeds,DxfDocument doc)
        { for(int i=0;i<seeds.Count;i++)Same(Scalars[i],((Insert)doc.GetObjectByHandle(seeds[i].Handle)).Scale,"Input scale bits "+i);Check(doc.Objects.Validate().Count==0,"Scalar graph"); }
        internal static void VerifyInstalled() { foreach(var item in All(null))item.Test(); }
    }
}
