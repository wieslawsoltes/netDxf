using netDxf.Entities;
using Att = netDxf.Entities.Attribute;

namespace netDxf.Shadow.NetDxf;

internal sealed partial record SourceMethodPlan
{
    internal static SourceMethodPlan? AttributeMembership(object instance, ShadowObject before, ShadowObject after,
        ShadowSnapshot baseline, ShadowSnapshot target, Func<ShadowValue, Type, object?> convert)
    {
        if (instance is Insert insertion && !ShadowValue.Equivalent(before["EndSequenceRecord"], after["EndSequenceRecord"]))
        {
            if (before["Attributes"] is ShadowValue.Reference reference && baseline.Objects.TryGetValue(reference.Id, out var oldCollection)
                && target.Objects.TryGetValue(reference.Id, out var nextCollection) && !ShadowValue.Equivalent(oldCollection["$items"], nextCollection["$items"]))
                return new(() => { }, () => { }, new HashSet<string> { "EndSequenceRecord" }); // The owned membership plan restores both in dependency order.
            var endBefore = insertion.EndSequenceRecord;
            var endAfter = (DxfObject?)convert(after["EndSequenceRecord"], typeof(DxfObject));
            return new(() => insertion.SetAttributeSequenceEnd(endAfter), () => insertion.SetAttributeSequenceEnd(endBefore), new HashSet<string> { "EndSequenceRecord" });
        }
        if (instance is not netDxf.Collections.AttributeCollection { Owner: { } source }
            || ShadowValue.Equivalent(before["$items"], after["$items"])) return null;
        if (!ShadowValue.Equivalent(before["Owner"], after["Owner"]) || before["Owner"] is not ShadowValue.Reference owner)
            throw new ArgumentException("Attribute collection ownership cannot be reassigned.");
        if (after["$items"] is not ShadowValue.Sequence) throw new ArgumentException("INSERT attributes require an ordered membership sequence.");
        var values = (Att[])convert(after["$items"], typeof(Att[]))!;
        // Validation is source-bound: a clone cannot adopt the retained live attributes.
        source.PrepareAttributeReplacement(values);
        var previous = source.Attributes.ToArray();
        var previousEnd = source.EndSequenceRecord;
        var nextEnd = (DxfObject?)convert(target.Objects[owner.Id]["EndSequenceRecord"], typeof(DxfObject));
        var proxy = source.ProxyGraphics;
        void Set(Att[] attributes, DxfObject? end)
        {
            if (end is not null && !ReferenceEquals(source.EndSequenceRecord, end)) source.SetAttributeSequenceEnd(end);
            source.ReplaceAttributes(attributes);
            if (attributes.Length == 0 && end is null) source.SetAttributeSequenceEnd(null);
        }
        return new(() => Set(values, nextEnd), () =>
        {
            Set(previous, previousEnd);
            source.ProxyGraphics = proxy;
        }, new HashSet<string>(StringComparer.Ordinal) { "$items", "Count" });
    }
}
