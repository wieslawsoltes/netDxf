// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] ImageAffineProxy = Enumerable.Range(0, 300).Select(i => (byte)(i * 17)).ToArray();
    private static readonly Matrix3[] ImageAffineMaps = {
        Matrix3.Identity, Matrix3.Identity, Matrix3.Scale(2, 3, 4),
        new(0,-1,0, 1,0,0, 0,0,1), new(1,.75,0, 0,1,0, 0,0,1),
        new(1,0,0, 0,1,0, .5,0,1), Matrix3.Scale(-1,1,1),
        Matrix3.Scale(1e-100), Matrix3.Scale(1e100)
    };
    private static Vector3 ImageTranslation(int map) => map == 0 || map >= 7 ? Vector3.Zero : new Vector3(11,-13,17);
    private static ImageDefinition ImageTestDefinition() => new("IA_DEF", "image-affine.png", 8, 96, 6, 96, ImageResolutionUnits.Inches);
    private static Image ImageAffineSubject(int plane, int skew)
    {
        var item = new Image(ImageTestDefinition(), new Vector3(5,-3,7), 4, 3)
        {
            Normal = plane == 0 ? Vector3.UnitZ : plane == 1 ? Vector3.UnitY : new Vector3(0,3,4),
            Uvector = skew == 0 ? Vector2.UnitX : new Vector2(.6,.8),
            Vvector = skew == 0 ? Vector2.UnitY : new Vector2(.8,.6),
            Clipping = true, Brightness = 61, Contrast = 37, Fade = 9,
            ClippingBoundary = new ClippingBoundary(new[] { new Vector2(.25,.5), new Vector2(6,.25), new Vector2(7,4), new Vector2(1,5) })
        };
        var data = new XData(new ApplicationRegistry("IMAGE_AFFINE_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); item.XData.Add(data);
        return item;
    }
    private static Vector3[] ImageWorldAxes(Image item)
    {
        var axes = MathHelper.ArbitraryAxis(item.Normal);
        return new[] { item.Position,
            axes * new Vector3(item.Uvector.X * item.Width, item.Uvector.Y * item.Width, 0),
            axes * new Vector3(item.Vvector.X * item.Height, item.Vvector.Y * item.Height, 0) };
    }
    private static void ImageNearVector(Vector3 expected, Vector3 actual, string message)
    {
        double scale = Math.Max(1e-280, Math.Max(Math.Abs(expected.X), Math.Max(Math.Abs(expected.Y), Math.Abs(expected.Z))));
        foreach (var pair in new[] { (expected.X, actual.X), (expected.Y, actual.Y), (expected.Z, actual.Z) })
            Check(double.IsFinite(pair.Item2) && Math.Abs(pair.Item1 - pair.Item2) <= 2e-12 * scale, message);
    }
    private static void ImageCheckAxes(Vector3[] expected, Image item)
    {
        var actual = ImageWorldAxes(item);
        for (int i = 0; i < 3; i++) ImageNearVector(expected[i], actual[i], "Image WCS position/pixel basis " + i);
    }
    private static long[] ImageGeometryBits(Image item) => new[] {
        item.Position.X,item.Position.Y,item.Position.Z,item.Normal.X,item.Normal.Y,item.Normal.Z,
        item.Uvector.X,item.Uvector.Y,item.Vvector.X,item.Vvector.Y,item.Width,item.Height
    }.Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static bool ImageProxyEqual(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
    private static void ImageApply(Image item, int map, bool matrix4)
    {
        if (matrix4) item.TransformBy(LineReviewMatrix4(ImageAffineMaps[map], ImageTranslation(map)));
        else item.TransformBy(ImageAffineMaps[map], ImageTranslation(map));
    }
    private static Vector3[] ImageExpected(int plane, int skew, string kind)
    {
        var input = ImageAffineSubject(plane, skew); var axes = ImageWorldAxes(input);
        if (kind == "R")
        {
            var ocs = MathHelper.ArbitraryAxis(input.Normal);
            return new[] { input.Position, ocs * new Vector3(0,4,0),
                ocs * (skew == 0 ? new Vector3(-3,0,0) : new Vector3(.84,2.88,0)) };
        }
        int m = int.Parse(kind, System.Globalization.CultureInfo.InvariantCulture);
        return new[] { ImageAffineMaps[m] * axes[0] + ImageTranslation(m), ImageAffineMaps[m] * axes[1], ImageAffineMaps[m] * axes[2] };
    }
    private static void RegisterImageAffineTests()
    {
        for (int plane = 0; plane < 3; plane++) for (int skew = 0; skew < 2; skew++)
        for (int map = 0; map < ImageAffineMaps.Length; map++) foreach (bool matrix4 in new[] { false,true })
        {
            int p = plane, s = skew, m = map;
            Run($"image-affine/api/{p}/{s}/{m}/{matrix4}", () => {
                var item = ImageAffineSubject(p,s); var doc = new DxfDocument(); doc.Entities.Add(item);
                item.ProxyGraphics = ImageAffineProxy; var before = ImageGeometryBits(item);
                var boundary = item.ClippingBoundary; var definition = item.Definition; var owner = item.Owner; string handle = item.Handle;
                var data = item.XData["IMAGE_AFFINE_KEEP"]; var original = ImageWorldAxes(item);
                var expected = new[] { ImageAffineMaps[m] * original[0] + ImageTranslation(m), ImageAffineMaps[m] * original[1], ImageAffineMaps[m] * original[2] };
                ImageApply(item,m,matrix4); ImageCheckAxes(expected,item);
                if (m == 0) Check(before.SequenceEqual(ImageGeometryBits(item)), "Image identity changed stored bits");
                Check(ImageProxyEqual(m == 0 ? ImageAffineProxy : null, item.ProxyGraphics), "Image transformed cache");
                Check(ReferenceEquals(boundary,item.ClippingBoundary) && ReferenceEquals(definition,item.Definition), "Image transform replaced clip/definition");
                Check(ReferenceEquals(owner,item.Owner) && item.Handle == handle && ReferenceEquals(data,item.XData["IMAGE_AFFINE_KEEP"]), "Image identity/metadata changed");
                var clone = (Image)item.Clone(); ImageCheckAxes(expected,clone);
                Check(!ReferenceEquals(clone.ClippingBoundary,item.ClippingBoundary) && !ReferenceEquals(clone.Definition,item.Definition), "Image clone aliases dependencies");
                clone.Width *= 2; ImageCheckAxes(expected,item);
                Equal(0,doc.Objects.Validate().Count,"Image graph");
            });
        }
        for (int basis = 0; basis < 4; basis++) foreach (double angle in new[] { 0.0,30.0,90.0,180.0,270.0,-45.0,765.0 })
        {
            int b = basis; double a = angle;
            Run($"image-affine/rotation/{b}/{a}", () => {
                var item = ImageAffineSubject(0,b % 2);
                if (b >= 2) { item.Uvector = -item.Uvector; item.Vvector = -item.Vvector; }
                var position = item.Position; var normal = item.Normal; var clip = item.ClippingBoundary;
                double dot = Vector2.DotProduct(item.Uvector,item.Vvector);
                double cross = item.Uvector.X * item.Vvector.Y - item.Uvector.Y * item.Vvector.X;
                item.ProxyGraphics = ImageAffineProxy; double old = item.Rotation;
                item.Rotation = a;
                Near(MathHelper.NormalizeAngle(a),item.Rotation,"Absolute image rotation");
                Near(dot,Vector2.DotProduct(item.Uvector,item.Vvector),"Image skew dot");
                Near(cross,item.Uvector.X * item.Vvector.Y - item.Uvector.Y * item.Vvector.X,"Image skew orientation");
                Check(ImageProxyEqual(MathHelper.NormalizeAngle(a) == old ? ImageAffineProxy : null,item.ProxyGraphics),"Rotation cache policy");
                item.ProxyGraphics = Array.Empty<byte>(); var bits = ImageGeometryBits(item);
                item.Rotation = a; item.Rotation = item.Rotation;
                Check(bits.SequenceEqual(ImageGeometryBits(item)) && ImageProxyEqual(Array.Empty<byte>(),item.ProxyGraphics),"Repeated angle assignment drifted");
                RawLinePointBits(position,item.Position); RawLinePointBits(normal,item.Normal);
                SameDoubleBits(4,item.Width,"Rotation width"); SameDoubleBits(3,item.Height,"Rotation height");
                Check(ReferenceEquals(clip,item.ClippingBoundary),"Rotation replaced pixel clip");
            });
        }
        foreach (double bad in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity })
        {
            double captured = bad;
            Run($"image-affine/reject/rotation/{ParameterBits(bad)}", () => {
                var item = ImageAffineSubject(2,1); ImageReject(item,() => item.Rotation = captured);
            });
            for (int component = 0; component < 16; component++)
            {
                int c = component;
                Run($"image-affine/reject/matrix4/{c}/{ParameterBits(bad)}", () => {
                    var item = ImageAffineSubject(2,1); var matrix = Matrix4.Identity; matrix[c/4,c%4] = captured;
                    ImageReject(item,() => item.TransformBy(matrix));
                });
            }
            for (int component = 0; component < 12; component++)
            {
                int c = component;
                Run($"image-affine/reject/matrix3/{c}/{ParameterBits(bad)}", () => {
                    var item = ImageAffineSubject(0,0); var matrix = Matrix3.Identity; var t = Vector3.Zero;
                    if(c < 9) matrix[c/3,c%3] = captured;
                    else t = c == 9 ? new Vector3(captured,0,0) : c == 10 ? new Vector3(0,captured,0) : new Vector3(0,0,captured);
                    ImageReject(item,() => item.TransformBy(matrix,t));
                });
            }
        }
        for (int c = 0; c < 4; c++)
        {
            int column = c;
            Run($"image-affine/reject/projective/{c}", () => {
                var item = ImageAffineSubject(0,0); var matrix = Matrix4.Identity; matrix[3,column] = column == 3 ? 2 : double.Epsilon;
                ImageReject(item,() => item.TransformBy(matrix));
            });
        }
        foreach (bool matrix4 in new[] { false,true }) for (int kind = 0; kind < 3; kind++)
        {
            int k = kind;
            Run($"image-affine/reject/rank-overflow/{k}/{matrix4}", () => {
                var item = ImageAffineSubject(0,0); var matrix = k == 0 ? Matrix3.Scale(0) : k == 1 ? Matrix3.Scale(1,0,1) : Matrix3.Scale(double.MaxValue);
                ImageReject(item,() => { if(matrix4)item.TransformBy(LineReviewMatrix4(matrix,Vector3.Zero));else item.TransformBy(matrix,Vector3.Zero); });
            });
        }
        for (int edit = 0; edit < 12; edit++) for (int cache = 0; cache < 3; cache++)
        {
            int e = edit, c = cache;
            Run($"image-affine/setter/{e}/{c}", () => {
                var item = ImageAffineSubject(0,0); byte[]? proxy = c == 0 ? null : c == 1 ? Array.Empty<byte>() : ImageAffineProxy;
                item.ProxyGraphics = proxy;
                switch(e) {
                    case 0:item.Position = item.Position;break;
                    case 1:item.Width = item.Width;break;
                    case 2:item.Height = item.Height;break;
                    case 3:item.Uvector = item.Uvector;break;
                    case 4:item.Vvector = item.Vvector;break;
                    case 5:item.Position += Vector3.UnitX;break;
                    case 6:item.Width *= 2;break;
                    case 7:item.Height *= 2;break;
                    case 8:item.Uvector = Vector2.UnitY;break;
                    case 9:item.Vvector = Vector2.UnitX;break;
                    case 10:Throws<ArgumentException>(() => item.Uvector = Vector2.Zero);break;
                    case 11:Throws<ArgumentOutOfRangeException>(() => item.Width = 0);break;
                }
                Check(ImageProxyEqual(e >= 5 && e <= 9 ? null : proxy,item.ProxyGraphics),"Geometry setter cache");
            });
        }
        Run("image-affine/normal-callback", () => {
            var item = new ImageNormalTrap();item.ProxyGraphics = ImageAffineProxy;
            item.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));
            Equal(0,item.Calls,"Image virtual Normal callback");Check(item.ProxyGraphics == null,"Image callback cache");
        });
        Run("image-affine/transform-callback", () => {
            var item = new ImageTransformTrap();var matrix = Matrix4.Identity;matrix.M44 = 2;
            Throws<NotSupportedException>(() => item.TransformBy(matrix));Equal(0,item.Calls,"Invalid matrix reached virtual affine entry");
            item.TransformBy(Matrix4.Identity);Equal(1,item.Calls,"Valid matrix lost virtual dispatch");
        });
        Run("image-affine/large-translation-retains-size", () => {
            var item = ImageAffineSubject(0,0);item.TransformBy(Matrix3.Scale(2,3,4),new Vector3(1e100,-1e100,1e100));
            SameDoubleBits(8,item.Width,"Large-offset width");SameDoubleBits(9,item.Height,"Large-offset height");
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false,true })
        for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"image-affine/wire/{version}/{binary}/{p}", () => {
                var doc = new DxfDocument(version);doc.Comments.Clear();var items = new List<Image>();
                for(int plane=0;plane<3;plane++)for(int skew=0;skew<2;skew++)for(int mode=0;mode<8;mode++){
                    string kind = mode == 7 ? "R" : mode.ToString();
                    var item = ImageAffineSubject(plane,skew);item.Layer = new Layer($"IA_{plane}_{skew}_{kind}");item.ProxyGraphics = ImageAffineProxy;
                    if(mode==7){item.Rotation=30;item.Rotation=90;item.Rotation=90;}else ImageApply(item,mode,(mode&1)!=0);
                    ImageCheckAxes(ImageExpected(plane,skew,kind),item);items.Add(item);
                }
                if(p==0)doc.Entities.Add(items);
                else if(p==1){doc.Layouts.Add(new Layout("IA_PAPER"));foreach(var item in items)doc.Layouts["IA_PAPER"].AssociatedBlock.Entities.Add(item);}
                else{var block = new Block("IA_HOLDER",items);if(p==2)doc.Entities.Add(new Insert(block));else doc.Blocks.Add(block);}
                doc.Entities.Add(new Line(new Vector3(17.25,-4.5,2),new Vector3(18.5,9.25,-3)));
                string[] handles = items.Select(i=>i.Handle).ToArray();
                string stem = $"image-affine-{version}-{binary}-{p}";
                using var source = new MemoryStream();Check(doc.Save(source,binary),"Image source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source.ToArray());source.Position=0;
                var loaded=DxfDocument.Load(source)??throw new InvalidOperationException("Image source load");ImageCheckDocument(loaded,handles);
                foreach(bool output in new[]{false,true}){
                    using var stream = new MemoryStream();Check(loaded.Save(stream,output),"Image resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    ImageCheckDocument(DxfDocument.Load(stream)??throw new InvalidOperationException("Image reload"),handles);
                }
                Check(source.CanRead,"Image load closed caller stream");
            });
        }
    }
    private static void ImageReject(Image item, Action action)
    {
        item.ProxyGraphics = ImageAffineProxy;var bits = ImageGeometryBits(item);var boundary = item.ClippingBoundary;var definition = item.Definition;
        bool rejected = false;
        try { action(); } catch(Exception ex) when(ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { rejected = true; }
        Check(rejected,"Invalid IMAGE operation was accepted");
        Check(bits.SequenceEqual(ImageGeometryBits(item)) && ReferenceEquals(boundary,item.ClippingBoundary) && ReferenceEquals(definition,item.Definition),"Rejected image operation changed state");
        Check(ImageProxyEqual(ImageAffineProxy,item.ProxyGraphics),"Rejected image operation cleared graphics");
    }
    private static void ImageCheckDocument(DxfDocument doc, string[] handles)
    {
        var images=doc.Blocks.SelectMany(b=>b.Entities).OfType<Image>().OrderBy(i=>i.Layer.Name,StringComparer.Ordinal).ToArray();
        Equal(48,images.Length,"Image wire inventory");
        for(int i=0;i<images.Length;i++){
            var item=images[i];var parts=item.Layer.Name.Split('_');int plane=int.Parse(parts[1]),skew=int.Parse(parts[2]);
            var expected=ImageExpected(plane,skew,parts[3]);ImageCheckAxes(expected,item);Equal(handles[i],item.Handle,"Image handle");
            Check(ImageProxyEqual(parts[3]=="0"?ImageAffineProxy:null,item.ProxyGraphics),"Image wire cache");
            var clip=ImageAffineSubject(0,0).ClippingBoundary;
            Equal(clip.Vertexes.Count,item.ClippingBoundary.Vertexes.Count,"Image pixel clip count");
            for(int j=0;j<clip.Vertexes.Count;j++){
                SameDoubleBits(clip.Vertexes[j].X,item.ClippingBoundary.Vertexes[j].X,"Image clip X");
                SameDoubleBits(clip.Vertexes[j].Y,item.ClippingBoundary.Vertexes[j].Y,"Image clip Y");
            }
            Equal((short)61,item.Brightness,"Image brightness");Equal((short)37,item.Contrast,"Image contrast");Equal((short)9,item.Fade,"Image fade");
            Check(item.Clipping,"Image clipping state");Equal(8,item.Definition.Width,"Image pixel width");Equal(6,item.Definition.Height,"Image pixel height");
            Equal("unchanged",(string)item.XData["IMAGE_AFFINE_KEEP"].XDataRecord.Single().Value,"Image XData");
            ImageCheckAxes(expected,(Image)item.Clone());
        }
        Equal(0,doc.Objects.Validate().Count,"Image wire graph");
        var line=doc.Entities.Lines.Single();RawLinePointBits(new Vector3(17.25,-4.5,2),line.StartPoint);RawLinePointBits(new Vector3(18.5,9.25,-3),line.EndPoint);
    }
    private sealed class ImageNormalTrap : Image
    {
        public int Calls;
        public ImageNormalTrap() : base(ImageTestDefinition(),Vector3.Zero,4,3) { }
        public override Vector3 Normal { get { Calls++;throw new InvalidOperationException("Unexpected image normal getter"); } set { Calls++;throw new InvalidOperationException("Unexpected image normal setter"); } }
    }
    private sealed class ImageTransformTrap : Image
    {
        public int Calls;
        public ImageTransformTrap() : base(ImageTestDefinition(),Vector3.Zero,4,3) { }
        public override void TransformBy(Matrix3 matrix,Vector3 translation) { Calls++; }
    }
}
