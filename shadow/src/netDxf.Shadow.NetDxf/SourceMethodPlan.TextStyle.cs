using System.Globalization;
using netDxf.Tables;

namespace netDxf.Shadow.NetDxf;

internal sealed partial record SourceMethodPlan
{
    internal static bool XDataGraphChanged(ShadowSnapshot before, ShadowSnapshot after, ObjectId style, ShadowGraphPatch patch)
    {
        static ShadowReader FontData(ShadowSnapshot snapshot, ObjectId id) => snapshot.Read(id)["XData"].Items.Select(entry => entry["Value"])
            .FirstOrDefault(data => data["ApplicationRegistry"]["Name"].Text.Equals("ACAD", StringComparison.OrdinalIgnoreCase));
        var beforeData = FontData(before, style); var afterData = FontData(after, style);
        if (beforeData.Id != afterData.Id) return true;
        var ids = new HashSet<ObjectId>();
        foreach (var data in new[] { beforeData, afterData })
        {
            if (data.Id is { } dataId) ids.Add(dataId);
            if (data["XDataRecord"].Id is { } recordsId) ids.Add(recordsId);
            foreach (var record in data["XDataRecord"].Items) if (record.Id is { } recordId) ids.Add(recordId);
        }
        return patch.Changes.Any(change => ids.Contains(change.Id));
    }
    private static readonly HashSet<string> FontProperties = new(StringComparer.Ordinal) { "FontFile", "BigFont", "ExtendedFontData", "FontFamilyName", "FontStyle" };
    // FontFile clears BigFont/extended metadata, while FontFamilyName clears file names.
    // Apply canonical font metadata together in a deterministic order, including undo/redo.
    private static SourceMethodPlan? TextStyleFonts(TextStyle source, ShadowObject before, ShadowObject after, bool xdataManaged)
    {
        if (new[] { "FontFile", "BigFont", "ExtendedFontData" }.All(n => ShadowValue.Equivalent(before[n], after[n]))) return null;
        string Text(string name) => after[name] is ShadowValue.Atom a ? a.Text : "";
        TextStyleFontData? data = null;
        if (after["ExtendedFontData"] is ShadowValue.Structure fields)
        {
            if (fields.Fields.GetValueOrDefault("FamilyName") is not ShadowValue.Atom family || fields.Fields.GetValueOrDefault("Flags") is not ShadowValue.Atom flags || !int.TryParse(flags.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
                throw new ArgumentException("Invalid canonical text style font metadata.");
            data = new(family.Text, bits);
        }
        var file = Text("FontFile"); var big = Text("BigFont");
        var oldFile = source.FontFile; var oldBig = source.BigFont;
        var extendedChanged = !ShadowValue.Equivalent(before["ExtendedFontData"], after["ExtendedFontData"]);
        var originalData = source.XData.Values.Select(x => (Data: x, Records: x.XDataRecord.ToArray())).ToArray();
        void Set(TextStyle style)
        {
            style.SetStoredFontFiles(file, big);
            // Full graph replay already contains the exact desired XData membership and
            // record identities. A second derived setter would replace those objects.
            if (extendedChanged && !xdataManaged) style.ExtendedFontData = data;
        }
        Set((TextStyle)source.Clone()); // Validate compound metadata before live mutation.
        return new(() => Set(source), () =>
        {
            source.SetStoredFontFiles(oldFile, oldBig);
            if (!extendedChanged || xdataManaged) return;
            source.XData.Clear();
            foreach (var item in originalData)
            {
                item.Data.XDataRecord.Clear(); item.Data.XDataRecord.AddRange(item.Records);
                source.XData.Add(item.Data);
            }
        }, FontProperties);
    }
}
