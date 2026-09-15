using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterRetainedRecordTargetRemovalTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (int role in Enumerable.Range(0, 9))
        foreach (bool xdata in new[] { false, true })
        foreach (bool containingBlock in new[] { false, true })
            Run($"retained-record-target/{version}/{binary}/{role}/{xdata}/{containingBlock}",
                () => RetainedRecordTargetRemoval(version, binary, role, xdata, containingBlock));
    }

    private static void RetainedRecordTargetRemoval(DxfVersion version, bool binary, int role, bool xdata, bool containingBlock)
    {
        var doc = new DxfDocument(version);
        EntityObject parent = role < 2
            ? new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY })
            : role < 4
                ? new PolygonMesh(2, 2, new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(1, 1, 0) })
                : role < 7
                    ? new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                        new[] { new PolyfaceMeshFace(new short[] { 1, -2, 3 }) })
                    : (Polyline2D)Legacy2DPolyline(StoredDimAssocLoad(Legacy2DInput(version, binary)), version, binary, true).Clone();
        doc.Entities.Add(parent);
        string parentHandle = parent.Handle;
        doc = StoredDimAssocLoad(StoredDimAssocSave(doc, binary));
        parent = (EntityObject)doc.GetObjectByHandle(parentHandle);
        DxfObject record = parent switch
        {
            Polyline3D p => role == 0 ? p.VertexRecords[0] : p.EndSequenceRecord,
            PolygonMesh p => role == 2 ? p.VertexRecords[0] : p.EndSequenceRecord,
            PolyfaceMesh p => role == 4 ? p.VertexRecords[0] : role == 5 ? p.FaceRecords[0] : p.EndSequenceRecord,
            Polyline2D p => role == 7 ? p.VertexRecords[0] : p.EndSequenceRecord,
            _ => throw new InvalidOperationException()
        };
        string recordHandle = record.Handle;
        var target = new Line(Vector3.Zero, new Vector3(8, 4, 0));
        Block? targetBlock = null;
        if (containingBlock)
        {
            targetBlock = new Block("RETAINED_TARGET"); targetBlock.Entities.Add(target); doc.Blocks.Add(targetBlock);
        }
        else doc.Entities.Add(target);
        string targetHandle = target.Handle;
        string? blockHandle = targetBlock?.Record.Handle;
        void Link()
        {
            if (xdata)
            {
                var data = new XData(new ApplicationRegistry("RETAINED_TARGET_LINK"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, targetHandle));
                record.XData.Add(data);
            }
            else record.PersistentReactors.Add(target);
        }
        void Clear()
        {
            if (xdata) record.XData.Remove("RETAINED_TARGET_LINK");
            else record.PersistentReactors.Clear();
        }
        bool RemoveTarget() => containingBlock ? doc.Blocks.Remove(targetBlock!) : doc.Entities.Remove(target);
        void AssertGuard(string stage)
        {
            Equal(0, doc.Objects.Validate().Count, stage + " valid graph");
            long seed = OwnershipSeed(doc);
            Check(!RemoveTarget(), stage + " allowed referenced ordinary target removal");
            Equal(seed, OwnershipSeed(doc), stage + " changed handle seed");
            Check(ReferenceEquals(doc.GetObjectByHandle(targetHandle), target), stage + " lost target registration");
            Check(ReferenceEquals(doc.GetObjectByHandle(recordHandle), record), stage + " lost retained record registration");
            Check(ReferenceEquals(doc.GetObjectByHandle(parentHandle), parent), stage + " lost parent registration");
            if (containingBlock) Check(ReferenceEquals(doc.GetObjectByHandle(blockHandle!), targetBlock!.Record), stage + " lost containing block registration");
            if (xdata) Equal(targetHandle, record.XData["RETAINED_TARGET_LINK"].XDataRecord.Single().Value, stage + " changed XData");
            else Check(ReferenceEquals(record.PersistentReactors.Single(), target), stage + " changed reactor identity");
        }
        Link();
        var unrelated = new Line(Vector3.Zero, Vector3.UnitZ); doc.Entities.Add(unrelated);
        Check(doc.Entities.Remove(unrelated), "unrelated entity removal was blocked");
        AssertGuard("authored metadata");
        doc = StoredDimAssocLoad(StoredDimAssocSave(doc, binary));
        parent = (EntityObject)doc.GetObjectByHandle(parentHandle);
        record = doc.GetObjectByHandle(recordHandle);
        target = (Line)doc.GetObjectByHandle(targetHandle);
        if (containingBlock) targetBlock = doc.Blocks["RETAINED_TARGET"];
        AssertGuard("reloaded metadata");
        Clear(); Check(RemoveTarget(), "clearing the last reference did not release target");
        Check(doc.GetObjectByHandle(targetHandle) == null, "released target stayed registered");

        target = new Line(Vector3.Zero, Vector3.UnitY);
        if (containingBlock)
        {
            targetBlock = new Block("RETAINED_TARGET_AGAIN"); targetBlock.Entities.Add(target); doc.Blocks.Add(targetBlock);
        }
        else doc.Entities.Add(target);
        targetHandle = target.Handle;
        Link();
        Check(doc.Entities.Remove(parent), "outgoing common metadata prevented clean parent detach");
        Check(doc.GetObjectByHandle(recordHandle) == null, "detached child remained registered");
        Check(RemoveTarget(), "detached record kept its former target guarded");
        Equal(0, doc.Objects.Validate().Count, "released graph validity");
    }
}
