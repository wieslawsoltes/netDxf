// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeSynchronizationTests()
    {
        Run("attribute-sync/replaced-definition-preserves-values-and-identity", () =>
        {
            var block = new Block("SYNC"); var oldDefinition = new AttributeDefinition("TAG") { Position = new Vector3(1, 2, 0) };
            block.AttributeDefinitions.Add(oldDefinition); var root = new Insert(block, new Vector3(10, 20, 0)) { Rotation = 90, Scale = new Vector3(2) };
            var doc = new DxfDocument(); doc.Entities.Add(root); var attribute = root.Attributes.Single();
            attribute.Value = "user value"; string handle = attribute.Handle;
            block.AttributeDefinitions.Remove("TAG");
            var replacement = new AttributeDefinition("TAG") { Position = new Vector3(4, 5, 0), Value = "new default" };
            block.AttributeDefinitions.Add(replacement); root.Sync();
            Check(ReferenceEquals(attribute, root.Attributes.Single()) && ReferenceEquals(attribute.Definition, replacement), "Sync replaced identity or retained stale definition");
            Equal(handle, attribute.Handle, "Sync changed handle"); Equal("user value", attribute.Value, "Sync overwrote user value");
            R12Vector(new Vector3(0, 28, 0), attribute.Position);
            root.Sync(); R12Vector(new Vector3(0, 28, 0), attribute.Position);
        });
        Run("attribute-sync/geometry-refusal-precedes-membership-and-binding", () =>
        {
            var (doc, root) = AmSeed(); var attribute = root.Attributes.Single();
            root.Block.AttributeDefinitions.Add(new AttributeDefinition("REMOVE")); root.Sync();
            var removed = root.Attributes.Single(a => a.Tag == "REMOVE"); var oldDefinition = attribute.Definition;
            var before = root.Attributes.ToArray(); string stamp = AtStamp(attribute.GetTextData()); Vector3 position = attribute.Position;
            root.Block.AttributeDefinitions.Remove("REMOVE"); root.Block.AttributeDefinitions.Remove("VALUE");
            var replacement = new AttributeDefinition("VALUE"); replacement.SetMText(new AttributeMText { Value = "new layout" });
            root.Block.AttributeDefinitions.Add(replacement); root.Block.AttributeDefinitions.Add(new AttributeDefinition("ADDED"));
            root.Scale = new Vector3(2, 3, 1);
            Throws<NotSupportedException>(() => root.Sync());
            Check(root.Attributes.SequenceEqual(before) && ReferenceEquals(attribute.Definition, oldDefinition), "Failed sync changed membership or binding");
            Check(removed.Owner == root && removed.Handle != null, "Failed sync detached an attribute");
            Equal(stamp, AtStamp(attribute.GetTextData()), "Failed sync changed embedded layout"); R12Vector(position, attribute.Position);
        });
        Run("attribute-sync/membership-events-and-proxy-follow-successful-sync", () =>
        {
            var (doc, root) = AmSeed(); var removed = root.Attributes.Single();
            root.Block.AttributeDefinitions.Remove("VALUE");
            var definition = new AttributeDefinition("NEW") { Value = "new value", Position = new Vector3(5, 6, 0) };
            root.Block.AttributeDefinitions.Add(definition); root.ProxyGraphics = new byte[] { 1, 2, 3 }; root.Sync();
            Check(removed.Owner == null && removed.Handle == null, "Removed attribute remained attached");
            var added = root.Attributes.Single();
            Check(added.Tag == "NEW" && added.Owner == root && added.Handle != null, "New attribute lacks native ownership/identity");
            Check(ReferenceEquals(added.Definition, definition) && root.ProxyGraphics == null, "New binding or proxy invalidation failed");
            Check(doc.TextStyles.Remove("BODY"), "Removed attribute retained embedded style usage");
            Equal("new value", AmLoad(AmBytes(doc, false)).Entities.Inserts.Single().Attributes.Single().Value, "Synced membership transport");
            root.Block.AttributeDefinitions.Remove("NEW"); root.ProxyGraphics = new byte[] { 4 }; root.Sync();
            Check(root.Attributes.Count == 0 && root.ProxyGraphics == null, "Removing last attribute retained membership/proxy");
        });
    }
}
