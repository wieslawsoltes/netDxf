// Test-only reflection runner. All results come from the pinned production assembly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Units;

internal static partial class Program
{
    private static object? Wire(object? value)
    {
        if (value is null) return null;
        if (value is double d) return new { @double = Bits(d) };
        if (value is long l) return new { @long = l.ToString(CultureInfo.InvariantCulture) };
        if (value is string textValue) return Utf16Wire(textValue);
        if (value is bool) return value;
        if (value is char character) return new {charCode=(int)character};
        if (DimensionWire(value,out var dimensionValue)) return dimensionValue;
        if (value is IEnumerator && value is not IEnumerable) return new {type="Enumerator"};
        if (value is KeyValuePair<string,netDxf.Entities.AttributeDefinition> pair) return new[]{Wire(pair.Key),Wire(pair.Value)};
        if (value is int || value is short || value is byte || value is Enum) return new { @double = Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture)) };
        if (value is DateTime date) return new { date = new[] {date.Year,date.Month,date.Day,date.Hour,date.Minute,date.Second,date.Millisecond}, ticks=date.Ticks.ToString(), kind=(int)date.Kind };
        if (value is TimeSpan span) return new { ticks = span.Ticks.ToString() };
        if (value is Vector2 v2) return new { type="Vector2", values=new[]{Bits(v2.X),Bits(v2.Y)}, normalized=v2.IsNormalized };
        if (value is Vector3 v3) return new { type="Vector3", values=new[]{Bits(v3.X),Bits(v3.Y),Bits(v3.Z)}, normalized=v3.IsNormalized };
        if (value is Vector4 v4) return new { type="Vector4", values=new[]{Bits(v4.X),Bits(v4.Y),Bits(v4.Z),Bits(v4.W)}, normalized=v4.IsNormalized };
        if (value is Matrix2 || value is Matrix3 || value is Matrix4) {
            var type = value.GetType(); int n=int.Parse(type.Name[^1..]);
            var cells = new List<string>();
            for(int r=1;r<=n;r++)for(int c=1;c<=n;c++)cells.Add(Bits((double)type.GetProperty($"M{r}{c}")!.GetValue(value)!));
            return new { type=type.Name, values=cells, identity=(bool)type.GetProperty("IsIdentity")!.GetValue(RuntimeHelpers.GetObjectValue(value))! };
        }
        if(value is BezierCurve curve) return new { type=value.GetType().Name, degree=curve.Degree, points=curve.ControlPoints.Select(v=>Wire(v)).ToArray() };
        if(value is BoundingRectangle box) return new { type="BoundingRectangle", min=Wire(box.Min),max=Wire(box.Max),center=Wire(box.Center),radius=Wire(box.Radius),width=Wire(box.Width),height=Wire(box.Height) };
        if(value is ClippingBoundary clip) return new { type="ClippingBoundary", kind=(int)clip.Type, vertices=clip.Vertexes.Select(v=>Wire(v)).ToArray() };
        if(value is AciColor color) return new {type="AciColor",r=color.R,g=color.G,b=color.B,index=color.Index,trueColor=color.UseTrueColor,byLayer=color.IsByLayer,byBlock=color.IsByBlock};
        if(value is netDxf.Entities.HatchPatternLineDefinition line) return new {type=value.GetType().Name,angle=Wire(line.Angle),origin=Wire(line.Origin),delta=Wire(line.Delta),dashes=line.DashPattern.Select(WireDouble).ToArray()};
        if(value is netDxf.Entities.HatchPattern hatchPattern) {
            var pattern=new {type=value.GetType().Name,name=hatchPattern.Name,description=hatchPattern.Description,style=(int)hatchPattern.Style,fill=(int)hatchPattern.Fill,
                kind=(int)hatchPattern.Type,isDouble=hatchPattern.IsDouble,origin=Wire(hatchPattern.Origin),angle=Wire(hatchPattern.Angle),scale=Wire(hatchPattern.Scale),
                lines=hatchPattern.LineDefinitions.Select(v=>Wire(v)).ToArray()};
            if(value is netDxf.Entities.HatchGradientPattern gradient) return new {pattern,gradientType=(int)gradient.GradientType,color1=Wire(gradient.Color1),color2=Wire(gradient.Color2),
                single=gradient.SingleColor,tint=Wire(gradient.Tint),shift=Wire(gradient.Shift),centered=gradient.Centered,aci1=Wire(gradient.Color1AciIndex),aci2=Wire(gradient.Color2AciIndex),
                auto1=gradient.IsColor1AciIndexAutomatic,auto2=gradient.IsColor2AciIndexAutomatic};
            return new {pattern};
        }
        if(value is XDataRecord record) return new {type="XDataRecord",code=(int)record.Code,value=Wire(record.Value)};
        if(value is DxfClass definition) return new {type="DxfClass",name=definition.Name,cpp=definition.CppClassName,application=definition.ApplicationName,flags=definition.ProxyFlags,count=definition.InstanceCount,wasProxy=definition.WasProxy,entity=definition.IsEntity};
        if(value is System.Drawing.Color rgba) return new {type="Color",argb=rgba.ToArgb(),name=rgba.Name,known=rgba.IsKnownColor,named=rgba.IsNamedColor,empty=rgba.IsEmpty};
        if(value is Transparency alpha) return new { type="Transparency", value=alpha.Value, stored=alpha.StoredAlphaValue, byLayer=alpha.IsByLayer, byBlock=alpha.IsByBlock };
        if (HeaderWire(value, out var headerValue)) return headerValue;
        if (LayoutViewportValueWire(value, out var layoutViewportValue)) return layoutViewportValue;
        if (BlockWire(value, out var blockValue)) return blockValue;
        if (GroupWire(value, out var groupValue)) return groupValue;
        if (OutputSettingsWire(value, out var outputValue)) return outputValue;
        if (MLineValueWire(value, out var mlineValue)) return mlineValue;
        if (HatchBoundaryWire(value, out var boundaryValue)) return boundaryValue;
        if (SurfaceWire(value, out var surfaceValue)) return surfaceValue;
        if (DatabaseModelWire(value, out var modelValue)) return modelValue;
        if (CoordinateWire(value, out var coordinateValue)) return coordinateValue;
        if (EntityWire(value, out var entityValue)) return entityValue;
        if (StyleWire(value, out var styleValue)) return styleValue;
        if (value is ITuple tuple) return Enumerable.Range(0,tuple.Length).Select(i=>Wire(tuple[i])).ToArray();
        if (value is IDictionary dict) return dict.Keys.Cast<object>().Select(k=>new[]{Wire(k),Wire(dict[k])}).ToArray();
        if (value is netDxf.IO.DxfTag tag) return new {code=tag.Code,value=Wire(tag.Value)};
        if (value is IEnumerable list) return list.Cast<object?>().Select(Wire).ToArray();
        throw new ArgumentException("Unmapped result type " + value.GetType().FullName);
    }
    private static object? WireDouble(double value) => Wire(value);
}
