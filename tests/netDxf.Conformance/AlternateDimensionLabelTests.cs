// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
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
    private static readonly string[] AltPrimary = { "8.6603", "10.0000", "90°", "Ø10.0000", "R5.0000", "90°", "2.0000", "7.8540" };
    private static readonly string[] AltDefault = { "219.97", "254.00", "", "254.00", "127.00", "", "50.80", "199.49" };
    private static readonly string[] AltOverride = { "17.321", "20.000", "", "20.000", "10.000", "", "4.000", "15.708" };
    private static string AltExpected(int kind, int variant) => AltPrimary[kind] +
        (kind is 2 or 5 || variant == 1 ? "" : variant == 2 ? "[ALT:" + AltOverride[kind] + "u]" : "[" + AltDefault[kind] + "mm]");

    private static Dimension AltLabelDimension(int kind, int variant)
    {
        var dim = TextBlockDimension(kind);
        dim.Style = (DimensionStyle)dim.Style.Clone("ALT_LABEL_" + variant);
        dim.UserText = "<>";
        dim.Layer = new Layer("ALT_LABEL_" + variant);
        dim.Style.AlternateUnits.Enabled = variant != 2;
        if (variant == 1) dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsEnabled, false);
        dim.Style.AlternateUnits.Suffix = "mm";
        if (variant == 2)
        {
            var overrides = dim.StyleOverrides;
            overrides.Add(DimensionStyleOverrideType.AltUnitsEnabled, true);
            overrides.Add(DimensionStyleOverrideType.AltUnitsMultiplier, 2.0);
            overrides.Add(DimensionStyleOverrideType.AltUnitsLengthPrecision, (short)3);
            overrides.Add(DimensionStyleOverrideType.AltUnitsPrefix, "ALT:");
            overrides.Add(DimensionStyleOverrideType.AltUnitsSuffix, "u");
        }
        if (variant == 3) dim.StyleOverrides.Add(DimensionStyleOverrideType.TextColor, new AciColor(3));
        return dim;
    }
    private static string AltLabel(Dimension dim) => DimensionBlock.Build(dim).Entities.OfType<MText>().Single().Value;

    private static void RegisterAlternateDimensionLabelTests()
    {
        for (int kind = 0; kind < 8; kind++) for (int variant = 0; variant < 4; variant++)
        {
            int k=kind,v=variant;
            Run($"alternate-label/api/{k}/{v}", () =>
            {
                var dim = AltLabelDimension(k,v); double measure = dim.Measurement;
                var alternate = dim.Style.AlternateUnits;
                string expected = AltExpected(k,v);
                Equal(expected, AltLabel(dim), "Alternate label");
                Equal(expected, TextBlockDirect(dim, "DIRECT").Entities.OfType<MText>().Single().Value, "Typed builder alternate label");
                dim.UserText = "A<>B<>C";
                Equal("A"+expected+"B"+expected+"C", AltLabel(dim), "Repeated placeholder");
                dim.UserText = "FIXED"; Equal("FIXED", AltLabel(dim), "Literal remains literal");
                dim.UserText = " "; Equal(0, DimensionBlock.Build(dim).Entities.OfType<MText>().Count(), "Suppression");
                Check(ReferenceEquals(alternate,dim.Style.AlternateUnits), "Base value-object identity");
                SameDoubleBits(25.4,alternate.Multiplier,"Base multiplier unchanged");
                SameDoubleBits(measure,dim.Measurement,"Formatting changed measurement");
                Equal("mm",alternate.Suffix,"Base suffix unchanged");
                var clone = (Dimension)dim.Clone(); clone.Style.AlternateUnits.Suffix = "changed";
                Equal("mm",alternate.Suffix,"Detached clone isolation");
            });
        }
        var modes = new[] { LinearUnitType.Scientific, LinearUnitType.Decimal, LinearUnitType.Engineering,
            LinearUnitType.Architectural, LinearUnitType.Fractional, LinearUnitType.WindowsDesktop };
        string[] expectedModes = { "1.25E+01", "12.50", "1'-0.50\"", "1'-0 1/2\"", "12 1/2", "12,50" };
        for (int index = 0; index < modes.Length; index++) foreach (bool viaOverride in new[] { false, true })
        {
            int i=index;
            Run($"alternate-label/format/{i}/{viaOverride}", () =>
            {
                var culture = CultureInfo.CurrentCulture;
                try
                {
                    var explicitCulture=(CultureInfo)CultureInfo.InvariantCulture.Clone();
                    explicitCulture.NumberFormat.NumberDecimalDigits=2; explicitCulture.NumberFormat.NumberDecimalSeparator=",";
                    CultureInfo.CurrentCulture=explicitCulture;
                    var dim = new AlignedDimension(Vector2.Zero,new Vector2(1.25,0),3,new DimensionStyle("ALT_FORMAT"));
                    var alt = dim.Style.AlternateUnits; alt.Enabled=true;alt.Multiplier=10;alt.LengthPrecision=2;
                    alt.LengthUnits=modes[i];alt.StackUnits=false;
                    if (viaOverride)
                    {
                        alt.LengthUnits=LinearUnitType.Scientific;
                        dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsLengthUnits,modes[i]);
                        dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsStackedUnits,false);
                    }
                    Equal("1.2500["+expectedModes[i]+"]", AltLabel(dim), "Unit mode");
                    Equal(",",CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,"Culture mutation");
                }
                finally { CultureInfo.CurrentCulture=culture; }
            });
        }
        foreach (bool architectural in new[] { false, true })
            Run("alternate-label/stacked/"+architectural, () =>
            {
                var dim = new AlignedDimension(Vector2.Zero,new Vector2(1.25,0),3,new DimensionStyle("ALT_STACK"));
                var alt=dim.Style.AlternateUnits;alt.Enabled=true;alt.Multiplier=10;alt.LengthPrecision=2;alt.StackUnits=true;
                alt.LengthUnits=architectural?LinearUnitType.Architectural:LinearUnitType.Fractional;
                dim.Style.TextFractionHeightScale=0.5;
                Equal(architectural ? "1.2500[{\\A1;1'-0{\\H0.5x;\\S1/2;}\"}]" : "1.2500[{\\A1;12{\\H0.5x;\\S1/2;}}]",AltLabel(dim),"Scoped stacked fraction");
            });
        for (int owner=0;owner<3;owner++) foreach (bool negative in new[] {false,true})
        {
            int p=owner;
            Run($"alternate-label/scale-round/{p}/{negative}",()=>
            {
                var dim=new AlignedDimension(Vector2.Zero,new Vector2(10.15,0),3,new DimensionStyle("ALT_ROUND"));
                dim.Style.DimScaleLinear=negative?-3:3;dim.Style.DimRoundoff=1;
                dim.Style.AlternateUnits.Enabled=true;dim.Style.AlternateUnits.Multiplier=2;dim.Style.AlternateUnits.Roundoff=0.5;
                var doc=new DxfDocument();
                if(p==1)doc.Entities.Add(dim);
                if(p==2){doc.Layouts.Add(new Layout("ALT_PAPER"));doc.Layouts["ALT_PAPER"].AssociatedBlock.Entities.Add(dim);}
                Equal(negative&&p!=2?"10.0000[20.50]":"30.0000[61.00]",AltLabel(dim),"Independent rounding and owner scale");
                SameDoubleBits(10.15,dim.Measurement,"Scaled dimension geometry");
            });
        }
        Run("alternate-label/suppression-overrides",()=>
        {
            var dim=new AlignedDimension(Vector2.Zero,new Vector2(0.125,0),3,new DimensionStyle("ALT_ZERO"));
            var alt=dim.Style.AlternateUnits;alt.Enabled=true;alt.Multiplier=4;alt.LengthPrecision=4;
            dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsSuppressLinearLeadingZeros,true);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsSuppressLinearTrailingZeros,true);
            Equal("0.1250[.5]",AltLabel(dim),"Independent alternate zero suppression");
            Check(!alt.SuppressLinearLeadingZeros&&!alt.SuppressLinearTrailingZeros,"Suppression edited base");
        });
        Run("alternate-label/inherited-overall-scale",()=>
        {
            var dim=AltLabelDimension(1,3);dim.Style.DimScaleOverall=3;
            var text=DimensionBlock.Build(dim).Entities.OfType<MText>().Single();
            SameDoubleBits(2.25,text.Height,"Unrelated override lost overall scale");
            SameDoubleBits(3,dim.Style.DimScaleOverall,"Base overall scale changed");
        });
        Run("alternate-label/alternate-roundoff-override",()=>
        {
            var dim=AltLabelDimension(1,0);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsRoundoff,10.0);
            Equal("10.0000[250.00mm]",AltLabel(dim),"Alternate rounding override");
            SameDoubleBits(0,dim.Style.AlternateUnits.Roundoff,"Base rounding edited");
        });
        Run("alternate-label/feet-inch-overrides",()=>
        {
            var dim=new AlignedDimension(Vector2.Zero,new Vector2(1,0),3,new DimensionStyle("ALT_FEET"));
            var alt=dim.Style.AlternateUnits;alt.Enabled=true;alt.Multiplier=12;alt.LengthUnits=LinearUnitType.Architectural;
            dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsSuppressZeroInches,false);
            dim.StyleOverrides.Add(DimensionStyleOverrideType.AltUnitsSuppressZeroFeet,false);
            Equal("1.0000[1'-0\"]",AltLabel(dim),"Zero-inch projection");
            Check(alt.SuppressZeroInches&&alt.SuppressZeroFeet,"Base zero feet/inches edited");
        });
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity})
            Run("alternate-label/nonfinite/"+ParameterBits(invalid),()=>
            {
                var dim=AltLabelDimension(1,0);dim.Style.AlternateUnits.Multiplier=invalid;
                Throws<ArgumentOutOfRangeException>(()=>DimensionBlock.Build(dim));
                Equal(null,dim.Block,"Failed direct build attached a block");
            });
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity})
            Run("alternate-label/nonfinite-roundoff/"+ParameterBits(invalid),()=>
            {
                var dim=AltLabelDimension(1,0);dim.Style.AlternateUnits.Roundoff=invalid;
                Throws<ArgumentOutOfRangeException>(()=>DimensionBlock.Build(dim));
                Equal(null,dim.Block,"Invalid roundoff published geometry");
            });
        Run("alternate-label/conversion-overflow",()=>
        {
            var dim=AltLabelDimension(1,0);dim.Style.AlternateUnits.Multiplier=double.MaxValue;
            Throws<ArgumentOutOfRangeException>(()=>DimensionBlock.Build(dim));
            SameDoubleBits(10,dim.Measurement,"Overflow mutated measurement");
        });
        Run("alternate-label/rounding-overflow",()=>
        {
            var dim=AltLabelDimension(1,0);dim.Style.AlternateUnits.Multiplier=1e300;
            dim.Style.AlternateUnits.Roundoff=1e-6;
            Check(AltLabel(dim).Contains("["),"Large finite alternate rendering");
            dim.Style.AlternateUnits.Multiplier=1e306;
            Throws<ArgumentOutOfRangeException>(()=>DimensionBlock.Build(dim));
        });
        Run("alternate-label/unknown-format",()=>
        {
            var dim=AltLabelDimension(1,0);dim.Style.AlternateUnits.LengthUnits=(LinearUnitType)99;
            Throws<ArgumentOutOfRangeException>(()=>DimensionBlock.Build(dim));
        });
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true})
        for(int kind=0;kind<8;kind++) for(int placement=0;placement<3;placement++)
        {
            if(kind==7&&version<DxfVersion.AutoCad2004)continue;
            int k=kind,p=placement;
            Run($"alternate-label/wire/{version}/{binary}/{k}/{p}",()=>
            {
                var doc=new DxfDocument(version){BuildDimensionBlocks=true};doc.Comments.Clear();
                var dimensions=Enumerable.Range(0,4).Select(v=>AltLabelDimension(k,v)).ToArray();
                if(p==0)foreach(var dim in dimensions)doc.Entities.Add(dim);
                else if(p==1){doc.Layouts.Add(new Layout("ALT_PAPER"));foreach(var dim in dimensions)doc.Layouts["ALT_PAPER"].AssociatedBlock.Entities.Add(dim);}
                else doc.Entities.Add(new Insert(new Block("ALT_HOLDER",dimensions)));
                var handles=dimensions.Select(d=>d.Handle).ToArray();
                string stem=$"alternate-label-{version}-{binary}-{k}-{p}";
                using var source=new MemoryStream();Check(doc.Save(source,binary),"Alternate source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source.ToArray());source.Position=0;
                var loaded=DxfDocument.Load(source)??throw new InvalidOperationException("Alternate load");
                CheckAlternateLabels(loaded,k,handles);
                foreach(bool output in new[]{false,true})
                {
                    using var stream=new MemoryStream();Check(loaded.Save(stream,output),"Alternate output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    CheckAlternateLabels(DxfDocument.Load(stream)??throw new InvalidOperationException("Alternate reload"),k,handles);
                }
                Check(source.CanRead,"Caller stream ownership");
            });
        }
    }
    private static void CheckAlternateLabels(DxfDocument doc,int kind,string[] handles)
    {
        var dimensions=doc.Blocks.SelectMany(b=>b.Entities).OfType<Dimension>().OrderBy(d=>d.Layer.Name,StringComparer.Ordinal).ToArray();
        Equal(4,dimensions.Length,"Alternate host count");
        for(int v=0;v<4;v++)
        {
            var dim=dimensions[v];Equal(handles[v],dim.Handle,"Host identity");
            for(int i=0;i<3;i++)
            {
                Equal(AltExpected(kind,v),dim.Block.Entities.OfType<MText>().Single().Value,"Stored alternate label");
                dim.Update();
            }
            Equal(AltExpected(kind,v),AltLabel((Dimension)dim.Clone()),"Cloned alternate label");
        }
        Equal(0,doc.Objects.Validate().Count,"Alternate graph validation");
    }
}
