using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterOpaqueHatchReleaseTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true }) foreach (string kind in new[] { "native", "producer" })
            foreach (string scenario in new[] { "retained", "unlink", "paths", "create-unlinked", "create-linked", "transform", "clone", "remove-hatch", "dirty-unlink", "dirty-clear", "dirty-remove", "update", "readd", "private-pointer" })
                Run($"opaque-hatch/{kind}/{version}/{binary}/{scenario}", () => OpaqueHatchRelease(kind, version, binary, scenario));
    }

    private static (DxfDocument Doc, DxfOpaqueEntity Source) OpaqueHatchInput(string kind, DxfVersion version, bool binary, bool privatePointer)
    {
        var raw = HatchSourceRaw(HatchSourceInput(kind, version, binary)); string target = kind == "native" ? "8E" : "3A0";
        var record = HatchSourceRecord(raw, target); var tags = record.Tags.ToList(); tags[0] = new DxfTag(0, "FUTURE_BOUNDARY_CURVE");
        string hatch = (string)raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "HATCH").Tags.First(t => t.Code == 5).Value;
        int at = tags.FindIndex(t => t.Code == 1001); if (at < 0) at = tags.Count;
        // Declared-schema controls: an unqualified application group stays inert even
        // when its value spells a HATCH handle; a flat standard pointer stays active.
        tags.InsertRange(at, new[] { new DxfTag(102, "{PRIVATE_RELEASE"), new DxfTag(330, hatch), new DxfTag(102, "}") });
        if (privatePointer) tags.Insert(at, new DxfTag(340, hatch));
        var doc = HatchSourceLoad(HatchSourceRawBytes(raw.WithRecord(record, tags), binary));
        return (doc, (DxfOpaqueEntity)doc.GetObjectByHandle(target));
    }

    private static string OpaqueHatchTags(IEnumerable<DxfTag> tags) => string.Join("|", tags.Select(t => t.Code + ":" +
        (t.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture))));

    private static void OpaqueHatchPacket(DxfOpaqueEntity source, IReadOnlyList<DxfTag> original, byte[] output)
    {
        Equal(OpaqueHatchTags(original), OpaqueHatchTags(source.SourceTags), "Original opaque SourceTags changed");
        var expected = new List<DxfTag>(); bool reactors = false;
        foreach (var tag in original)
        {
            if (tag.Code == 102) reactors = Equals(tag.Value, "{ACAD_REACTORS");
            if (reactors && tag.Code == 330 && !source.PersistentReactors.Any(r => string.Equals(r.Handle, (string)tag.Value, StringComparison.OrdinalIgnoreCase))) continue;
            expected.Add(tag);
        }
        Equal(OpaqueHatchTags(expected), OpaqueHatchTags(HatchSourceRecord(HatchSourceRaw(output), source.SourceHandle).Tags),
            "Only explicitly released qualified HATCH backlink fields may leave the original packet");
    }

    private static void OpaqueHatchRelease(string kind, DxfVersion version, bool binary, string scenario)
    {
        var pair = OpaqueHatchInput(kind, version, binary, scenario == "private-pointer"); var doc = pair.Doc; var source = pair.Source;
        var original = source.SourceTags.ToArray();
        var hatches = doc.Entities.Hatches.Where(h => h.BoundaryPaths.Any(p => p.Entities.Contains(source))).ToArray();
        var first = hatches[0]; var block = source.Owner; string sourceHandle = source.Handle;
        void Export(string suffix)
        {
            foreach (bool written in new[] { false, true })
            {
                byte[] bytes = HatchSourceSave(doc, written, $"opaque-hatch-{kind}-{version}-{binary}-{scenario}-{suffix}-{written}.dxf");
                var loaded = HatchSourceLoad(bytes);
                if (source.Owner != null)
                {
                    OpaqueHatchPacket(source, original, bytes);
                    var retained = (DxfOpaqueEntity)loaded.GetObjectByHandle(sourceHandle);
                    Equal(source.Reactors.Count, retained.Reactors.Count, "Reloaded managed source occurrences");
                    Equal(source.PersistentReactors.Count, retained.PersistentReactors.Count, "Reloaded persistent source backlinks");
                }
                else Check(loaded.GetObjectByHandle(sourceHandle) == null, "Retired source was reintroduced");
            }
        }
        void RemoveEmpty()
        { foreach (var hatch in doc.Entities.Hatches.Where(h => h.BoundaryPaths.Count == 0).ToArray()) Check(doc.Entities.Remove(hatch), "Empty HATCH can be removed after backlink release"); }
        if (scenario == "retained")
        {
            Check(ReferenceEquals(doc.GetObjectByHandle(sourceHandle), source) && ReferenceEquals(first.Owner, source.Owner), "Actual retained source identity");
            Equal(hatches.Sum(h => h.BoundaryPaths.Sum(p => p.Entities.Count(e => ReferenceEquals(e, source)))), source.Reactors.Count, "Every duplicate occurrence is bound");
            Check(!doc.Entities.Remove(source), "Live source removal must reject");
        }
        else if (scenario == "unlink")
        {
            foreach (var hatch in hatches) { hatch.UnLinkBoundary(); Export(hatch.Handle); }
            Check(source.Owner == block && source.Reactors.Count == 0 && source.PersistentReactors.Count == 0, "Explicit unlink retains source and releases final backlinks");
            Check(hatches.All(h => !source.References.Contains(h)), "Released qualified backlinks leave References");
        }
        else if (scenario == "paths" || scenario == "readd")
        {
            foreach (var hatch in hatches)
                foreach (var path in hatch.BoundaryPaths.Where(p => p.Entities.Contains(source)).ToArray())
                {
                    Check(hatch.BoundaryPaths.Remove(path), "Remove source path");
                    if (scenario == "readd")
                    {
                        int count = source.Reactors.Count; int paths = hatch.BoundaryPaths.Count;
                        Throws<NotSupportedException>(() => hatch.BoundaryPaths.Add(path));
                        Check(paths == hatch.BoundaryPaths.Count && count == source.Reactors.Count, "Rejected re-add changed paths or reactors");
                    }
                    RemoveEmpty(); Export(hatch.Handle + "-" + hatch.BoundaryPaths.Count);
                }
            Check(source.Owner == null && !block.Entities.Contains(source), "Last unreferenced source path removal retires the opaque entity");
        }
        else if (scenario.StartsWith("create-", StringComparison.Ordinal))
        {
            var boundary = first.CreateBoundary(scenario == "create-linked");
            Check(boundary.Count > 0 && boundary.All(e => e is not DxfOpaqueEntity), "Boundary recreation uses stored HATCH edges");
            Check(source.Owner == block && !source.Reactors.Contains(first), "Recreation releases only the old source association");
        }
        else if (scenario == "transform")
        {
            first.TransformBy(Matrix3.Identity, new Vector3(2, 3, 0));
            Check(!first.Associative && source.Owner == block && !source.Reactors.Contains(first), "HATCH transform releases source without projecting unknown geometry");
        }
        else if (scenario == "clone")
        {
            var before = source.Reactors.ToArray(); var copy = (Hatch)first.Clone();
            Check(!copy.Associative && copy.BoundaryPaths.All(p => p.Entities.Count == 0) && before.SequenceEqual(source.Reactors), "HATCH clone copies edges and leaves source untouched");
            doc.Entities.Add(copy);
        }
        else if (scenario == "remove-hatch")
        {
            foreach (var hatch in hatches) { Check(doc.Entities.Remove(hatch), "Qualified source backlink permits HATCH removal"); Export(hatch.Handle ?? "removed"); }
            Check(source.Owner == block && source.Reactors.Count == 0, "HATCH removal retains opaque source");
        }
        else if (scenario.StartsWith("dirty-", StringComparison.Ordinal))
        {
            var added = new Circle(Vector2.Zero, 2); doc.Entities.Add(added); source.PersistentReactors.Add(added);
            string snapshot = HatchSourceSnapshot(doc); var reactors = source.Reactors.ToArray(); var persistent = source.PersistentReactors.ToArray(); long seed = OwnershipSeed(doc);
            Action action = scenario == "dirty-unlink" ? () => first.UnLinkBoundary() : scenario == "dirty-clear" ? () => first.BoundaryPaths.Clear() : () => doc.Entities.Remove(first);
            Throws<NotSupportedException>(action);
            Check(snapshot == HatchSourceSnapshot(doc) && reactors.SequenceEqual(source.Reactors) && persistent.SequenceEqual(source.PersistentReactors)
                && seed == OwnershipSeed(doc) && first.Owner == block && block.Entities.Contains(first), "Dirty metadata refusal must precede HATCH, collection and identity mutations");
            source.PersistentReactors.Remove(added); first.UnLinkBoundary();
        }
        else if (scenario == "update")
        {
            var path = first.BoundaryPaths.First(p => p.Entities.Contains(source)); var edges = path.Edges.ToArray(); var entities = path.Entities.ToArray(); var flags = path.PathType;
            Throws<NotSupportedException>(() => path.Update());
            Check(edges.SequenceEqual(path.Edges) && entities.SequenceEqual(path.Entities) && flags == path.PathType, "Opaque geometry refusal precedes edge clearing");
        }
        else if (scenario == "private-pointer")
        {
            string before = HatchSourceSnapshot(doc); Check(!doc.Entities.Remove(first), "Flat private standard pointer must continue blocking HATCH removal"); Equal(before, HatchSourceSnapshot(doc), "Guarded removal changed HATCH");
            first.UnLinkBoundary(); Check(source.References.Contains(first) && !doc.Entities.Remove(first), "Authorized backlink release cannot release a private standard pointer");
        }
        Export("final");
        Equal(OpaqueHatchTags(original), OpaqueHatchTags(source.SourceTags), "SourceTags remain immutable after every lifecycle operation");
    }
}
