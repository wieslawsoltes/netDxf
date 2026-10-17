using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;

internal static class CapturePerformanceVerification
{
    private static readonly Type CaptureType = typeof(ShadowSession).Assembly.GetType(
        "netDxf.Shadow.NetDxf.ShadowCapture", throwOnError: true)!;
    private static readonly MethodInfo PreviewMethod = CaptureType.GetMethod("Preview")!;
    private static readonly Type AccessorsType = typeof(ShadowSession).Assembly.GetType(
        "netDxf.Shadow.NetDxf.SourceAccessors", throwOnError: true)!;
    private static readonly MethodInfo FindMethod = AccessorsType.GetMethod("Find")!;

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("capture/optimized values retain exact reflected public state", ValueParity));
        tests.Add(("capture/common values retain signed zero and normalization state", DistinctValueState));
        tests.Add(("capture/vector fast path retains nesting-budget failures", ValueBudget));
        tests.Add(("capture/immutable metadata and references are shared across owners", SharedMetadata));
        tests.Add(("capture/refresh observes mutations after common-value sharing", ChangedValues));
        tests.Add(("capture/shared scalar maps retain independent source edits and stored alpha bits", SharedObjectState));
        tests.Add(("capture/empty extended data transitions retain the full reachable graph", ExtendedDataTransitions));
        tests.Add(("capture/property-map sharing is bounded and retains every distinct color", BoundedPropertyMaps));
        tests.Add(("accessors/direct getters retain virtual dispatch and exceptions", GetterSemantics));
        tests.Add(("accessors/value getters operate on a copy while setters write through", ValueGetterSemantics));
        tests.Add(("capture/value field snapshots follow reference-mutating getters", ReferenceFieldGetterSemantics));
    }

    private static void ValueParity()
    {
        var capture = NewCapture();
        var numbers = new[]
        {
            0.0, -0.0, 1.0, -1.0, double.Epsilon, -double.Epsilon,
            double.MaxValue, double.MinValue, Math.PI, 1.0000000000000002,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000017UL))
        };
        foreach (var value in numbers)
        {
            AssertParity(capture, value);
            AssertParity(capture, new Vector2(value, -value));
            AssertParity(capture, new Vector3(value, -value, value / 2));
        }
        foreach (var value in new object[]
        {
            true, false, Vector2.Zero, Vector2.UnitX, Vector2.UnitY,
            Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            new Vector2(1, 0), new Vector2(0, 1), new Vector3(1, 0, 0),
            new Vector3(0, 1, 0), new Vector3(0, 0, 1),
            new ValueCarrier { Count = 7, Coordinate = 13 },
            new DictionaryEntry("entry", 11)
        }) AssertParity(capture, value);

        // All other primitive formatting remains on the generic capture path.
        foreach (var value in new object[]
        {
            (byte)255, (sbyte)-128, short.MinValue, ushort.MaxValue, int.MinValue,
            uint.MaxValue, long.MinValue, ulong.MaxValue, decimal.MaxValue,
            0.0f, -0.0f, float.Epsilon, float.NaN, 'x', "", "coordinate"
        }) AssertParity(capture, value);
    }

    private static void DistinctValueState()
    {
        var capture = NewCapture();
        Require(!ShadowValue.Equivalent(Preview(capture, 0.0), Preview(capture, -0.0)), "Signed zero was collapsed.");
        Require(!ShadowValue.Equivalent(Preview(capture, Vector3.UnitZ), Preview(capture, new Vector3(0, 0, 1))),
            "The normalization flag was collapsed.");
        Require(!ShadowValue.Equivalent(Preview(capture, Vector2.Zero), Preview(capture, new Vector2(-0.0, 0))),
            "A vector component lost its signed zero.");
        foreach (var value in new object[] { true, false, 0.0, -0.0, 1.0, -1.0, Vector2.UnitX, Vector3.UnitZ })
            Require(ReferenceEquals(Preview(capture, value), Preview(capture, value)), "A common detached value was allocated repeatedly.");
    }

    private static void ValueBudget()
    {
        var capture = NewCapture(new ShadowCaptureOptions(MaximumValueDepth: 1));
        AssertParity(capture, Vector3.UnitZ);
        try
        {
            Preview(capture, new[] { Vector3.UnitZ });
            throw new Exception("A nested vector exceeded the budget without failing.");
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException nested
            && nested.Message == "Shadow value nesting budget exceeded.") { }
    }

    private static void SharedMetadata()
    {
        var document = new DxfDocument();
        for (var index = 0; index < 32; index++)
            document.Entities.Add(new Line(new Vector2(index, 0), new Vector2(index, 1)));
        var session = new ShadowSession(document);
        var snapshot = session.Current;
        var lines = snapshot.Objects.Values.Where(value => value.Type == nameof(Line)).ToArray();
        Require(lines.Length == 32);
        Require(lines.All(value => ReferenceEquals(value.WritableProperties, lines[0].WritableProperties)),
            "Identical source types rebuilt writable metadata for each object.");
        Require(lines.All(value => ReferenceEquals(value["Layer"], lines[0]["Layer"])),
            "Repeated references to the same live object rebuilt detached identity values.");
        Require(ReferenceEquals(snapshot, session.Refresh()), "Sharing changed no-op refresh behavior.");
    }

    private static void ChangedValues()
    {
        var document = new DxfDocument();
        var line = new Line(Vector3.Zero, Vector3.UnitX);
        document.Entities.Add(line);
        var session = new ShadowSession(document);
        var before = session.Current;
        var id = before.EntityIds.Single();
        line.StartPoint = new Vector3(-0.0, 0, 0);
        line.EndPoint = new Vector3(1, 0, 0);
        var after = session.Refresh();
        Require(!ReferenceEquals(before, after));
        Require(ShadowValue.Equivalent(before.Objects[id]["StartPoint"], Reflected(Vector3.Zero)));
        Require(ShadowValue.Equivalent(after.Objects[id]["StartPoint"], Reflected(line.StartPoint)));
        Require(ShadowValue.Equivalent(after.Objects[id]["EndPoint"], Reflected(line.EndPoint)));
        Require(!ShadowValue.Equivalent(before.Objects[id]["EndPoint"], after.Objects[id]["EndPoint"]));
        Require(ReferenceEquals(after, session.Refresh()));
    }

    private static void GetterSemantics()
    {
        var source = new GetterDerived();
        var getter = Getter(typeof(GetterBase), nameof(GetterBase.Number));
        Require((int)getter(source)! == 19, "An open getter bypassed virtual dispatch.");
        var failure = Getter(typeof(GetterDerived), nameof(GetterDerived.Failure));
        try { failure(source); throw new Exception("A throwing getter was accepted."); }
        catch (InvalidOperationException error) { Require(ReferenceEquals(error, source.Error), "The getter exception was replaced or wrapped."); }
    }

    private static void SharedObjectState()
    {
        var document = new DxfDocument();
        var first = new Line(Vector2.Zero, Vector2.UnitX)
        {
            Color = new AciColor(17, 23, 41),
            Transparency = Transparency.FromAlphaValue(unchecked((int)0xad080099))
        };
        var second = new Line(Vector2.UnitY, Vector2.UnitX)
        {
            Color = new AciColor(17, 23, 41),
            Transparency = Transparency.FromAlphaValue(unchecked((int)0xad080099))
        };
        document.Entities.Add(first); document.Entities.Add(second);
        var session = new ShadowSession(document);
        var before = session.Current;
        var firstNode = before.Objects[new ObjectId("H:" + first.Handle)];
        var secondNode = before.Objects[new ObjectId("H:" + second.Handle)];
        var firstColor = ((ShadowValue.Reference)firstNode["Color"]).Id;
        var secondColor = ((ShadowValue.Reference)secondNode["Color"]).Id;
        var firstAlpha = ((ShadowValue.Reference)firstNode["Transparency"]).Id;
        var secondAlpha = ((ShadowValue.Reference)secondNode["Transparency"]).Id;
        Require(firstColor != secondColor && firstAlpha != secondAlpha, "Distinct source objects were merged.");
        Require(ReferenceEquals(before.Objects[firstColor].Properties, before.Objects[secondColor].Properties));
        Require(ReferenceEquals(before.Objects[firstAlpha].Properties, before.Objects[secondAlpha].Properties));
        var unchangedColor = Reflected(second.Color);
        var unchangedAlpha = Reflected(second.Transparency);
        first.Color.Index = 2;
        first.Color.UseTrueColor = true;
        first.Transparency.Value = 25;
        var after = session.Refresh();
        Require(ShadowValue.Equivalent(after.Objects[firstColor].Properties, ((ShadowValue.Structure)Reflected(first.Color)).Fields));
        Require(ShadowValue.Equivalent(after.Objects[firstAlpha].Properties, ((ShadowValue.Structure)Reflected(first.Transparency)).Fields));
        Require(ShadowValue.Equivalent(after.Objects[secondColor].Properties, ((ShadowValue.Structure)unchangedColor).Fields));
        Require(ShadowValue.Equivalent(after.Objects[secondAlpha].Properties, ((ShadowValue.Structure)unchangedAlpha).Fields));
        Require(ReferenceEquals(before.Objects[secondColor], after.Objects[secondColor]));
        Require(ReferenceEquals(before.Objects[secondAlpha], after.Objects[secondAlpha]));
        Require(ReferenceEquals(after, session.Refresh()));
    }

    private static void ExtendedDataTransitions()
    {
        var document = new DxfDocument();
        var first = new Line(Vector2.Zero, Vector2.UnitX);
        var second = new Line(Vector2.Zero, Vector2.UnitY);
        document.Entities.Add(first); document.Entities.Add(second);
        var session = new ShadowSession(document);
        var initial = session.Current;
        var entity = initial.Objects[new ObjectId("H:" + first.Handle)];
        var dataId = ((ShadowValue.Reference)entity["XData"]).Id;
        var initialProperties = initial.Objects[dataId].Properties;
        var application = new ApplicationRegistry("CaptureState");
        var data = new XData(application);
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "retained value"));
        first.XData.Add(data);
        var populated = session.Refresh();
        Require(populated.Objects[dataId]["Count"] is ShadowValue.Atom { TypeName: "System.Int32", Text: "1" });
        Require(populated.Objects[dataId]["$items"] is ShadowValue.Sequence { Items.Length: 1 });
        var valueIds = populated.Objects[dataId]["Values"].References().ToArray();
        Require(valueIds.Length == 1 && populated.Objects.ContainsKey(valueIds[0]));
        first.XData.Clear();
        var emptied = session.Refresh();
        Require(ReferenceEquals(emptied.Objects[dataId].Properties, initialProperties));
        Require(!emptied.Objects.ContainsKey(valueIds[0]), "An unreachable extended-data value was retained.");
        Require(ReferenceEquals(emptied, session.Refresh()));
    }

    private static void BoundedPropertyMaps()
    {
        var document = new DxfDocument();
        var lines = new List<Line>();
        for (var index = 0; index < 768; index++)
        {
            var line = new Line(new Vector2(index, 0), new Vector2(index, 1))
            {
                Color = new AciColor((byte)index, (byte)(index >> 8), (byte)(index * 31))
            };
            lines.Add(line); document.Entities.Add(line);
        }
        var session = new ShadowSession(document);
        foreach (var line in lines)
        {
            var entity = session.Current.Objects[new ObjectId("H:" + line.Handle)];
            var color = session.Current.Objects[((ShadowValue.Reference)entity["Color"]).Id];
            Require(ShadowValue.Equivalent(color.Properties, ((ShadowValue.Structure)Reflected(line.Color)).Fields));
        }
        var capture = typeof(ShadowSession).GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        var cache = capture.GetType().GetField("propertyCache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(capture)!;
        var entries = (IDictionary)cache.GetType().GetField("properties", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
        Require(entries.Count <= 256, "Capture retained an unbounded dictionary of distinct source states.");
    }

    private static void ValueGetterSemantics()
    {
        object source = new ValueCarrier { Count = 7, Coordinate = 13 };
        var getter = Getter(typeof(ValueCarrier), nameof(ValueCarrier.Mutating));
        Require((int)getter(source)! == 8);
        Require((int)getter(source)! == 8, "A value getter mutated the boxed source instead of its unboxed copy.");
        Require(((ValueCarrier)source).Count == 7);
        var accessor = FindMethod.Invoke(null, [typeof(ValueCarrier), nameof(ValueCarrier.Coordinate)])!;
        var setter = (Action<object, object?>)accessor.GetType().GetProperty("Set")!.GetValue(accessor)!;
        setter(source, 29);
        Require(((ValueCarrier)source).Coordinate == 29, "A boxed setter updated a copy.");
    }

    private static void ReferenceFieldGetterSemantics()
    {
        var actualSource = new ReferenceCarrier { Items = [7] };
        var expectedSource = new ReferenceCarrier { Items = [7] };
        var actual = Preview(NewCapture(), actualSource);
        var expected = Reflected(expectedSource);
        Require(ShadowValue.Equivalent(actual, expected), "The field retained its value from before the getter mutated its referenced collection.");
        Require(actual is ShadowValue.Structure structure
            && structure.Fields["Items"] is ShadowValue.Sequence { Items.Length: 2 }
            && structure.Fields["ZAppend"] is ShadowValue.Atom { Text: "2" });
        Require(actualSource.Items.SequenceEqual(new[] { 7, 23 }) && expectedSource.Items.SequenceEqual(actualSource.Items));
    }

    private static object NewCapture(ShadowCaptureOptions? options = null) => Activator.CreateInstance(CaptureType, options ?? new())!;
    private static ShadowValue Preview(object capture, object value) => (ShadowValue)PreviewMethod.Invoke(capture, [value])!;
    private static Func<object, object?> Getter(Type type, string name)
    {
        var accessor = FindMethod.Invoke(null, [type, name])!;
        return (Func<object, object?>)accessor.GetType().GetProperty("Get")!.GetValue(accessor)!;
    }
    private static void AssertParity(object capture, object value) =>
        Require(ShadowValue.Equivalent(Preview(capture, value), Reflected(value)), $"Captured {value.GetType().Name} differs from reflected public state.");

    // Independent public-reflection oracle, retaining the original invariant textual
    // contract without the optimized capture delegates or common-value tables.
    private static ShadowValue Reflected(object? value)
    {
        if (value is null) return ShadowValue.Null;
        var type = value.GetType();
        if (value is string text) return new ShadowValue.Atom(type.FullName!, text);
        if (type.IsPrimitive || type.IsEnum || value is decimal)
        {
            var representation = value switch
            {
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            return new ShadowValue.Atom(type.FullName!, representation ?? string.Empty);
        }
        if (!type.IsValueType && value is IEnumerable sequence)
            return new ShadowValue.Sequence(sequence.Cast<object?>().Select(Reflected).ToImmutableArray());
        var fields = ImmutableDictionary.CreateBuilder<string, ShadowValue>(StringComparer.Ordinal);
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(value => value.GetMethod?.IsPublic == true && value.GetIndexParameters().Length == 0))
            fields[property.Name] = Reflected(property.GetValue(RuntimeHelpers.GetObjectValue(value)));
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            fields[field.Name] = Reflected(field.GetValue(value));
        return new ShadowValue.Structure(type.FullName!, fields.ToImmutable());
    }

    private static void Require(bool condition, string message = "Capture performance regression failed.")
    {
        if (!condition) throw new Exception(message);
    }

    private class GetterBase { public virtual int Number => 3; }
    private sealed class GetterDerived : GetterBase
    {
        public override int Number => 19;
        public readonly InvalidOperationException Error = new("Getter failure.");
        public int Failure => throw Error;
    }
    private struct ValueCarrier
    {
        public int Count;
        public int Coordinate { get; set; }
        public int Mutating => ++Count;
    }
    private struct ReferenceCarrier
    {
        public List<int> Items;
        public int ZAppend { get { Items.Add(23); return Items.Count; } }
    }
}
