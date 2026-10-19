// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;
namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeTextDataTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            for (int profile = 0; profile < 6; profile++)
            {
                int p = profile;
                Run($"attribute-data/independent-packet/{p}/{binary}", () =>
                {
                    var doc = DxfDocument.Load(AmFixture(p, binary))!;
                    var root = doc.Entities.Inserts.Single(); var attribute = root.Attributes.Single();
                    var definition = root.Block.AttributeDefinitions[attribute.Tag];
                    var data = attribute.GetTextData(); var definitionData = definition.GetTextData();
                    string stamp = AtStamp(data), definitionStamp = AtStamp(definitionData);
                    attribute.SetTextData(null); definition.SetTextData(null);
                    Check(!attribute.HasMText && attribute.IsPositionLocked == null, "Text packet was not removed");
                    attribute.SetTextData(data); definition.SetTextData(definitionData);
                    Equal(stamp, AtStamp(attribute.GetTextData()), "Restored attribute packet");
                    Equal(definitionStamp, AtStamp(definition.GetTextData()), "Restored definition packet");
                    var loaded = AmLoad(AmBytes(doc, !binary)).Entities.Inserts.Single();
                    // The native writer emits the DXF 2018 version marker for embedded content.
                    data.Version ??= 0; definitionData.Version ??= 0;
                    Equal(AtStamp(data), AtStamp(loaded.Attributes.Single().GetTextData()), "Roundtrip attribute packet");
                    Equal(AtStamp(definitionData), AtStamp(loaded.Block.AttributeDefinitions[attribute.Tag].GetTextData()), "Roundtrip definition packet");
                });
            }
            Run("attribute-data/auxiliary-and-style/" + binary, () =>
            {
                var (doc, root) = AmSeed(); var attribute = root.Attributes.Single();
                var data = attribute.GetTextData(); data.Version = 0; data.FieldLength = 123;
                data.SecondaryFlag = 7; data.SecondaryAlignment = new Vector3(3, 4, 5);
                data.Auxiliary.Add(new DxfTag(1, "stored Ω")); data.Auxiliary.Add(new DxfTag(310, new byte[] { 0, 255, 42 }));
                data.Content.Style = new TextStyle("INSTANCE_ONLY", "romans.shx");
                attribute.ProxyGraphics = new byte[] { 1, 2, 3 }; attribute.SetTextData(data);
                string stamp = AtStamp(attribute.GetTextData());
                data.Content.Value = "caller edit"; data.Auxiliary.Clear(); data.Content.Style.Name = "caller style";
                Equal(stamp, AtStamp(attribute.GetTextData()), "Caller mutated adopted packet");
                Check(attribute.ProxyGraphics.SequenceEqual(new byte[] { 1, 2, 3 }), "Exact packet erased proxy");
                Check(ReferenceEquals(attribute.GetTextData(false).Content.Style, doc.TextStyles["INSTANCE_ONLY"]), "Noncloning view lost canonical style");
                Check(!ReferenceEquals(attribute.GetTextData().Content.Style, doc.TextStyles["INSTANCE_ONLY"]), "Default view leaked style");
                Check(!doc.TextStyles.Remove("INSTANCE_ONLY"), "Embedded style references were not registered");
                Equal(stamp, AtStamp(AmLoad(AmBytes(doc, binary)).Entities.Inserts.Single().Attributes.Single().GetTextData()), "Auxiliary transport");
                attribute.SetTextData(null); Check(doc.TextStyles.Remove("INSTANCE_ONLY"), "Removed packet retained style reference");
            });
        }
        foreach (bool directionLast in new[] { false, true }) Run("attribute-data/ordered-fields/" + directionLast, () =>
        {
            var tags = new[] { new DxfTag(50, 90.0), new DxfTag(11, 2.0), new DxfTag(21, 0.0), new DxfTag(31, 0.0) };
            if (!directionLast) tags = tags.Skip(1).Concat(tags.Take(1)).ToArray();
            var content = AttributeMText.FromStoredFields("text", TextStyle.Default, tags);
            Equal(string.Join(",", tags.Select(t => t.Code)), string.Join(",", content.GetStoredFields().Select(t => t.Code)), "Stored orientation order");
            Near(directionLast ? 0 : 90, content.ToMText().Rotation, "Effective ordered orientation");
            Equal(4, content.GetStoredFields().Count, "Absent layout fields became explicit defaults");
            Throws<ArgumentException>(() => AttributeMText.FromStoredFields("text", TextStyle.Default, tags.Concat(tags.Take(1))));
            Throws<System.IO.InvalidDataException>(() => AttributeMText.FromStoredFields("text", TextStyle.Default, new[] { new DxfTag(10, 1.0) }));
        });
        for (int fault = 0; fault < 8; fault++)
        {
            int f = fault;
            Run("attribute-data/refusal-atomic/" + f, () =>
            {
                var (_, root) = AmSeed(); var attribute = root.Attributes.Single();
                string before = AtStamp(attribute.GetTextData()); var data = attribute.GetTextData();
                switch (f)
                {
                    case 0: data.Version = 2; break;
                    case 1: data.Kind = 4; break;
                    case 2: data.Content = null!; break;
                    case 3: data.SecondaryAlignment = new Vector3(double.NaN, 0, 0); break;
                    case 4: data.Auxiliary.Add(new DxfTag(1005, "AB")); break;
                    case 5: data.Auxiliary.Add(new DxfTag(100, "AcDbXrecord")); break;
                    case 6: for (int i = 0; i < 4097; i++) data.Auxiliary.Add(new DxfTag(70, (short)0)); break;
                    case 7: data.Auxiliary.Add(null!); break;
                }
                Throws<ArgumentException>(() => attribute.SetTextData(data));
                Equal(before, AtStamp(attribute.GetTextData()), "Invalid packet changed live state");
            });
        }

    }
    private static string AtStamp(AttributeTextData data) => string.Join("|", data.Version, data.Kind, data.SecondaryFlag,
        data.FieldLength, data.Locked, data.SecondaryAlignment, data.Content?.Value, data.Content?.Style.Name,
        string.Join(";", (data.Content?.GetStoredFields() ?? Array.Empty<DxfTag>()).Concat(data.Auxiliary).Select(t =>
            $"{t.Code}:{t.ValueType}:{(t.Value is byte[] bytes ? Convert.ToHexString(bytes) : t.Value)}")));
}
