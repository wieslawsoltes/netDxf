// Test-only snapshots. Production C# implements all state transitions and text parsing.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
internal static partial class Program
{
    private static object Utf16Wire(string value)
    {
        for(int i=0;i<value.Length;i++) {
            char c=value[i];
            if(char.IsHighSurrogate(c)) { if(i+1<value.Length && char.IsLowSurrogate(value[i+1])) { i++;continue; } }
            else if(!char.IsLowSurrogate(c)) continue;
            return new {utf16=value.Select(c=>(int)c).ToArray()};
        }
        return value;
    }
    private static readonly HashSet<object> ActiveColumns = new(ReferenceEqualityComparer.Instance);
    private static bool MTextValueWire(object value,out object? result)
    {
        result=null;
        if(value is MTextColumns c) {
            if(!ActiveColumns.Add(c)) { result=new {type="MTextColumns",cycle=true};return true; }
            try { result=new {type="MTextColumns",kind=(int)c.Type,storage=(int)c.Storage,count=c.Count,auto=c.AutoHeight,reversed=c.FlowReversed,
                width=Wire(c.Width),gutter=Wire(c.Gutter),defined=Wire(c.DefinedHeight),totalWidth=Wire(c.TotalWidth),totalHeight=Wire(c.TotalHeight),
                storedWidth=Wire(c.StoredTotalWidth),direction=Wire(c.EmbeddedTextDirection),insertion=Wire(c.EmbeddedInsertionPoint),referenceWidth=Wire(c.EmbeddedReferenceWidth),
                heights=c.Heights.Select(h=>Wire(h)).ToArray(),links=c.LinkedColumns.Select(Wire).ToArray()};
            } finally { ActiveColumns.Remove(c); }
        }
        else if(value is MTextBackgroundFill b) result=new {type="MTextBackgroundFill",flags=(int)b.Flags,scale=Wire(b.ScaleFactor),aci=Wire(b.ColorIndex),rgb=Wire(b.TrueColor),name=b.ColorName,transparency=Wire(b.Transparency)};
        else if(value is MTextFormattingOptions f) result=new {type="MTextFormattingOptions",bold=f.Bold,italic=f.Italic,overline=f.Overline,underline=f.Underline,strike=f.StrikeThrough,
            superscript=f.Superscript,subscript=f.Subscript,color=Wire(f.Color),font=f.FontName,height=Wire(f.HeightFactor),scriptHeight=Wire(f.SuperSubScriptHeightFactor),oblique=Wire(f.ObliqueAngle),spacing=Wire(f.CharacterSpaceFactor),width=Wire(f.WidthFactor)};
        else if(value is MTextParagraphOptions p) result=new {type="MTextParagraphOptions",height=Wire(p.HeightFactor),alignment=(int)p.Alignment,vertical=(int)p.VerticalAlignment,before=Wire(p.SpacingBefore),after=Wire(p.SpacingAfter),
            first=Wire(p.FirstLineIndent),left=Wire(p.LeftIndent),right=Wire(p.RightIndent),spacing=Wire(p.LineSpacingFactor),style=(int)p.LineSpacingStyle};
        else return false;
        return true;
    }
}
