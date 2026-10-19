using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using Att = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeReplacementTests()
    {
        Run("attribute-membership/reorder-retains-collection-identities-and-values", () =>
        {
            var block = new Block("REPLACE"); block.AttributeDefinitions.Add(new AttributeDefinition("A")); block.AttributeDefinitions.Add(new AttributeDefinition("B"));
            var root = new Insert(block); var doc = new DxfDocument(); doc.Entities.Add(root);
            var collection = root.Attributes; var a = collection[0]; var b = collection[1]; var handles = new[] { a.Handle, b.Handle };
            a.Value = "authored"; a.Position = new Vector3(10, 20, 0); var events = 0;
            root.AttributeAdded += (_, _) => events++; root.AttributeRemoved += (_, _) => events++;
            root.ReplaceAttributes(new[] { b, a });
            Check(ReferenceEquals(root.Attributes, collection) && root.Attributes[0] == b && root.Attributes[1] == a, "Membership replaced surviving objects or the collection");
            Check(a.Handle == handles[0] && b.Handle == handles[1] && events == 0 && a.Value == "authored", "Reordering changed attributes or fired add/remove events");
            R12Vector(new Vector3(10, 20, 0), a.Position);
            root.ProxyGraphics = new byte[] { 1 }; root.ReplaceAttributes(root.Attributes);
            Check(root.ProxyGraphics != null, "Unchanged membership invalidated proxy data");
        });
        Run("attribute-membership/ownership-duplicates-and-late-enumeration-fail-before-mutation", () =>
        {
            var block = new Block("REFUSE"); block.AttributeDefinitions.Add(new AttributeDefinition("A")); var root = new Insert(block); var other = new Insert(block);
            var original = root.Attributes.Single(); var notifications = 0; root.AttributeRemoved += (_, _) => notifications++;
            IEnumerable<Att> Broken() { yield return original; throw new InvalidOperationException("enumeration failed"); }
            Throws<ArgumentException>(() => root.ReplaceAttributes(new[] { original, original }));
            Throws<ArgumentException>(() => root.ReplaceAttributes(new[] { other.Attributes.Single() }));
            Throws<ArgumentException>(() => root.ReplaceAttributes(new Att[] { null! }));
            Throws<InvalidOperationException>(() => root.ReplaceAttributes(Broken()));
            Check(root.Attributes.Single() == original && original.Owner == root && notifications == 0, "Rejected membership mutated the insertion");
        });
        Run("attribute-membership/prepared-plan-is-bound-to-current-membership-and-owner", () =>
        {
            var block = new Block("PREPARE"); var root = new Insert(block); var a = new Att(new AttributeDefinition("A"));
            var plan = root.PrepareAttributeReplacement(new[] { a }); root.ReplaceAttributes(new[] { new Att(new AttributeDefinition("B")) });
            Throws<InvalidOperationException>(() => plan.Apply()); Check(a.Owner == null, "Stale plan adopted an attribute");
            var next = root.PrepareAttributeReplacement(new[] { a }); new DxfDocument().Entities.Add(root);
            Throws<InvalidOperationException>(() => next.Apply()); Check(a.Owner == null, "Moved owner accepted a stale membership plan");
        });
        Run("attribute-membership/duplicate-tags-remain-distinct-instances", () =>
        {
            var root = new Insert(new Block("DUPLICATES")); var definition = new AttributeDefinition("TAG");
            var a = new Att(definition) { Value = "first" }; var b = new Att(definition) { Value = "second" };
            root.ReplaceAttributes(new[] { a, b }); Check(root.Attributes.Count == 2 && a.Value != b.Value, "Duplicate tags conflated instance values");
        });
        Run("attribute-membership/native-registration-precedes-early-application-notifications", () =>
        {
            var root = new Insert(new Block("NOTIFY")); var doc = new DxfDocument(); var observed = 0;
            root.AttributeAdded += (_, e) =>
            {
                Check(root.Attributes.Contains(e.Item) && e.Item.Owner == root && e.Item.Handle != null, "Notification observed uncommitted membership");
                Check(ReferenceEquals(e.Item.Style, doc.TextStyles["AddedFont"]), "Application notification ran before native resource registration");
                observed++; throw new InvalidOperationException("application notification");
            };
            doc.Entities.Add(root);
            var first = new Att(new AttributeDefinition("FIRST")) { Style = new TextStyle("AddedFont", "txt.shx") };
            var second = new Att(new AttributeDefinition("SECOND")) { Style = new TextStyle("AddedFont", "txt.shx") };
            Throws<AggregateException>(() => root.ReplaceAttributes(new[] { first, second }));
            Check(observed == 2 && root.Attributes.Count == 2 && !doc.TextStyles.Remove("AddedFont"), "Failed notification interrupted native adoption");
            root.ReplaceAttributes(Array.Empty<Att>());
            Check(first.Owner == null && first.Handle == null && doc.TextStyles.Remove("AddedFont"), "Removal leaked native resource references");
        });
        Run("attribute-membership/reentrant-notification-cannot-overwrite-committed-membership", () =>
        {
            var root = new Insert(new Block("REENTRANT")); var a = new Att(new AttributeDefinition("A"));
            root.AttributeAdded += (_, _) => root.ReplaceAttributes(Array.Empty<Att>());
            Throws<AggregateException>(() => root.ReplaceAttributes(new[] { a }));
            Check(root.Attributes.Single() == a && a.Owner == root, "Reentrant callback changed native membership");
        });
        Run("attribute-membership/canonical-resource-adoption-is-not-a-property-edit-callback", () =>
        {
            var root = new Insert(new Block("ADOPTION")); var doc = new DxfDocument(); doc.Entities.Add(root);
            var a = new Att(new AttributeDefinition("A")) { Layer = new Layer("Canonical"), Style = new TextStyle("Canonical", "txt.shx") };
            int callbacks = 0;
            a.LayerChanged += (_, _) => { callbacks++; throw new InvalidOperationException("Authored layer edit callback"); };
            a.TextStyleChanged += (_, _) => { callbacks++; throw new InvalidOperationException("Authored style edit callback"); };
            root.ReplaceAttributes(new[] { a });
            Check(callbacks == 0 && ReferenceEquals(a.Layer, doc.Layers["Canonical"]) && ReferenceEquals(a.Style, doc.TextStyles["Canonical"]), "Adoption dispatched authored property edits");
            Throws<InvalidOperationException>(() => a.Layer = new Layer("Changed"));
            Check(callbacks == 1 && a.Layer.Name == "Canonical", "An actual authored property edit bypassed callbacks");
        });
        foreach (bool binary in new[] { false, true }) Run("attribute-membership/resources-packets-and-sequence-survive-transport/" + binary, () =>
        {
            var (doc, root) = AmSeed(); var removed = root.Attributes.Single(); var end = root.EndSequenceRecord;
            var definition = new AttributeDefinition("NEW"); root.Block.AttributeDefinitions.Add(definition);
            var a = new Att(definition) { Value = "fallback", Layer = new Layer("NewLayer") };
            a.SetMText(new AttributeMText { Value = "embedded\\Ptext", Style = new TextStyle("NewEmbedded", "txt.shx") });
            root.ReplaceAttributes(new[] { a });
            Check(removed.Owner == null && removed.Handle == null && ReferenceEquals(end, root.EndSequenceRecord), "Replacement lost native membership metadata");
            var loaded = AmLoad(AmBytes(doc, binary)).Entities.Inserts.Single().Attributes.Single();
            Check(loaded.Tag == "NEW" && loaded.Value == "fallback" && loaded.GetMText().Value == "embedded\\Ptext" && loaded.Layer.Name == "NewLayer", "Replacement lost independent values or resources");
        });
        Run("attribute-membership/incoming-table-reference-refuses-preparation", () =>
        {
            var tags = ReviewTablePayload(); tags.Add(new netDxf.IO.DxfTag(340, "ABCDEF"));
            var doc = ReviewTableLoad(tags, false, targetKind: "attribute"); var root = doc.Entities.Inserts.Single(); var a = root.Attributes.Single();
            Throws<InvalidOperationException>(() => root.PrepareAttributeReplacement(Array.Empty<Att>()));
            Check(root.Attributes.Single() == a && a.Owner == root, "Referenced attribute was detached");
        });
        Run("attribute-membership/sequence-removal-and-restoration-preserve-object-identity", () =>
        {
            var block = new Block("END"); block.AttributeDefinitions.Add(new AttributeDefinition("A")); var root = new Insert(block); var doc = new DxfDocument(); doc.Entities.Add(root);
            var end = root.EndSequenceRecord; var handle = end.Handle;
            Throws<InvalidOperationException>(() => root.SetAttributeSequenceEnd(null));
            root.ReplaceAttributes(Array.Empty<Att>()); root.SetAttributeSequenceEnd(null);
            Check(root.EndSequenceRecord == null && end.Owner == null && doc.GetObjectByHandle(handle) == null, "Empty sequence remained registered");
            root.SetAttributeSequenceEnd(end);
            Check(root.EndSequenceRecord == end && end.Owner == root && ReferenceEquals(doc.GetObjectByHandle(end.Handle), end), "Sequence identity was not restored");
        });
        foreach (string kind in new[] { "reactor", "xdata", "xrecord" }) Run("attribute-membership/exposed-incoming-reference-prevents-removal/" + kind, () =>
        {
            var block = new Block("REFERENCES"); block.AttributeDefinitions.Add(new AttributeDefinition("A")); var root = new Insert(block); var doc = new DxfDocument(); doc.Entities.Add(root);
            var a = root.Attributes[0]; var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line);
            if (kind == "reactor") line.PersistentReactors.Add(a);
            else if (kind == "xdata")
            {
                var data = new XData(new ApplicationRegistry("ATTR_REF")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, a.Handle)); line.XData.Add(data);
            }
            else
            {
                var record = new netDxf.Objects.DxfXRecord(); record.Data.Add(new netDxf.IO.DxfTag(340, a.Handle)); doc.Objects.Root.Add("ATTR_REF", record);
            }
            Throws<InvalidOperationException>(() => root.ReplaceAttributes(Array.Empty<Att>()));
            Check(root.Attributes.Single() == a && a.Owner == root, "An exposed reference was orphaned");
        });
        Run("attribute-membership/definition-rebinding-preserves-values-and-checks-tag", () =>
        {
            var original = new AttributeDefinition("TAG"); var replacement = new AttributeDefinition("tag") { Value = "new" };
            var a = new Att(original) { Value = "instance" }; a.SetDefinition(replacement);
            Check(a.Definition == replacement && a.Value == "instance" && a.Tag == "TAG", "Definition rebinding overwrote instance data");
            Throws<ArgumentException>(() => a.SetDefinition(new AttributeDefinition("OTHER"))); Check(a.Definition == replacement, "Rejected binding changed definition");
            a.SetDefinition(null); Check(a.Definition == null && a.Value == "instance", "Orphan conversion changed authored value");
        });
        Run("attribute-sync/definition-justification-is-restored", () =>
        {
            var block = new Block("ALIGN"); var definition = new AttributeDefinition("A") { Alignment = TextAlignment.TopRight }; block.AttributeDefinitions.Add(definition);
            var root = new Insert(block); root.Attributes[0].Alignment = TextAlignment.BaselineLeft; root.Sync();
            Check(root.Attributes[0].Alignment == TextAlignment.TopRight, "Sync retained overridden justification");
            root.Attributes[0].Alignment = TextAlignment.BaselineLeft; root.ProxyGraphics = new byte[] { 5 }; root.TransformAttributes();
            Check(root.Attributes[0].Alignment == TextAlignment.TopRight, "TransformAttributes retained overridden justification");
            Check(root.ProxyGraphics == null, "Attribute geometry synchronization retained the INSERT proxy");
        });
        Run("attribute-sync/single-line-transform-failure-does-not-partially-publish", () =>
        {
            var block = new Block("ATOMIC"); block.AttributeDefinitions.Add(new AttributeDefinition("A"));
            block.AttributeDefinitions.Add(new AttributeDefinition("B") { Position = new Vector3(1e308, 0, 0) });
            var root = new Insert(block); root.Position = new Vector3(1e308, 0, 0);
            var before = root.Attributes.Select(a => a.Position).ToArray(); root.ProxyGraphics = new byte[] { 1 };
            Check(ReviewTableReject(() => root.TransformAttributes()), "Overflowing attribute frame was accepted");
            Check(root.Attributes.Select(a => a.Position).SequenceEqual(before) && root.ProxyGraphics != null, "Failed preparation partially changed attributes or proxy");
        });
    }
}
