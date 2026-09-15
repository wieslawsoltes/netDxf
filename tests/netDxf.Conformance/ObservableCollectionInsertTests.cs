using System.Text.Json;
using netDxf;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterObservableCollectionInsertTests()
    {
        foreach (int index in new[] { 0, 1, 3 })
        {
            Run($"observable-insert/generic/{index}", () => ObservableInsertGeneric(index, "success"));
            Run($"observable-insert/cancel/{index}", () => ObservableInsertGeneric(index, "cancel"));
            Run($"observable-insert/throw/{index}", () => ObservableInsertGeneric(index, "throw"));
        }
        foreach (int index in new[] { -1, 4 }) Run($"observable-insert/invalid/{index}", () => ObservableInsertGeneric(index, "invalid"));
        Run("observable-insert/empty", () => ObservableInsertGeneric(0, "empty"));
        Run("observable-insert/removal-events", ObservableInsertRemovalEvents);
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true }) foreach (int index in new[] { 0, 1, 2 })
            Run($"observable-insert/hatch/{version}/{binary}/{index}", () => ObservableInsertHatch(version, binary, index));
    }
    private static void ObservableInsertGeneric(int index, string mode)
    {
        var items = new ObservableCollection<int>(); if (mode != "empty") items.AddRange(new[] { 10, 20, 30 });
        var before = items.ToArray(); var events = new List<string>();
        items.BeforeAddItem += (_, e) => { Check(items.SequenceEqual(before), "BeforeAdd sees unchanged sequence"); events.Add("before-add:" + e.Item); if (mode == "cancel") e.Cancel = true; if (mode == "throw") throw new NotSupportedException("Caller refuses insertion"); };
        items.AddItem += (_, e) => { events.Add("add:" + e.Item); Equal(99, items[index], "Add sees inserted item"); };
        items.BeforeRemoveItem += (_, e) => events.Add("before-remove:" + e.Item);
        items.RemoveItem += (_, e) => events.Add("remove:" + e.Item);
        if (mode == "invalid") Throws<ArgumentOutOfRangeException>(() => items.Insert(index, 99));
        else if (mode == "cancel") Throws<ArgumentException>(() => items.Insert(index, 99));
        else if (mode == "throw") Throws<NotSupportedException>(() => items.Insert(index, 99));
        else items.Insert(index, 99);
        if (mode is "success" or "empty")
        {
            var expected = before.ToList(); expected.Insert(index, 99); Check(items.SequenceEqual(expected), "Insertion retains existing order and members");
            Check(events.SequenceEqual(new[] { "before-add:99", "add:99" }), "Insertion emits only addition callbacks");
        }
        else
        {
            Check(items.SequenceEqual(before), "Rejected insertion leaves sequence unchanged");
            Check(events.SequenceEqual(mode == "invalid" ? Array.Empty<string>() : new[] { "before-add:99" }), "Rejected insertion does not emit removal/addition callbacks");
        }
    }
    private static void ObservableInsertRemovalEvents()
    {
        var items = new ObservableCollection<int>(); items.AddRange(new[] { 10, 20, 30 }); var events = new List<string>();
        items.BeforeRemoveItem += (_, e) => events.Add("before:" + e.Item); items.RemoveItem += (_, e) => events.Add("removed:" + e.Item);
        Check(items.Remove(20), "Ordinary removal still succeeds"); Check(items.SequenceEqual(new[] { 10, 30 }), "Ordinary removal changes only requested member");
        Check(events.SequenceEqual(new[] { "before:20", "removed:20" }), "Removal callback semantics unchanged");
    }
    private static void ObservableInsertHatch(DxfVersion version, bool binary, int index)
    {
        var document = HatchSourceLoad(HatchSourceInput("producer", version, binary)); var hatch = (Hatch)document.GetObjectByHandle("3A2");
        var originals = hatch.BoundaryPaths.ToArray(); Equal(2, originals.Length, "Actual producer path inventory");
        var originalEntities = document.Entities.All.ToArray(); var sources = originals.SelectMany(p => p.Entities).Distinct().ToArray();
        var reactors = sources.ToDictionary(e => e, e => e.Reactors.ToArray()); var persistent = sources.ToDictionary(e => e, e => e.PersistentReactors.ToArray());
        var snapshots = originals.Select(p => (HatchBoundaryPath)p.Clone()).ToArray(); var newSources = new[] { new Polyline2D(new[] { new Vector2(20, 30), new Vector2(24, 30), new Vector2(24, 34), new Vector2(20, 34) }, true) };
        var inserted = new HatchBoundaryPath(newSources); int additions = 0, removals = 0;
        hatch.HatchBoundaryPathAdded += (_, _) => additions++; hatch.HatchBoundaryPathRemoved += (_, _) => removals++;
        string stem = $"observable-insert-{version}-{binary}-{index}"; Exception? failure = null;
        try { hatch.BoundaryPaths.Insert(index, inserted); } catch (Exception ex) { failure = ex; }
        var observations = new { index, exception = failure?.ToString(), additions, removals, paths = hatch.BoundaryPaths.Select(p => p.Entities.Select(e => e.Handle).ToArray()).ToArray(), sources = sources.Select(e => new { handle = e.Handle, expected_reactors = reactors[e].Select(r => r.Handle).ToArray(), actual_reactors = e.Reactors.Select(r => r.Handle).ToArray() }).ToArray() };
        File.WriteAllText(Path.Combine(ArtifactDirectory, stem + ".json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        if (failure != null) throw failure;
        HatchSourceSave(document, binary, stem + ".dxf");
        Equal(1, additions, "Exactly one HATCH path addition"); Equal(0, removals, "Insertion does not remove an existing HATCH path");
        var expected = originals.ToList(); expected.Insert(index, inserted); Check(hatch.BoundaryPaths.SequenceEqual(expected), "Existing HATCH path identity and order retained");
        for (int i = 0; i < originals.Length; i++)
        {
            Check(originals[i].Edges.Count == snapshots[i].Edges.Count && originals[i].PathType == snapshots[i].PathType, "Stored original edge packet inventory unchanged");
            Check(originals[i].Entities.All(e => originalEntities.Contains(e)), "Stored source identity retained");
        }
        foreach (var source in sources)
        {
            Check(source.Reactors.SequenceEqual(reactors[source]) && source.PersistentReactors.SequenceEqual(persistent[source]), "Existing source managed/persistent reactor occurrences retained");
            Check(ReferenceEquals(source.Owner, hatch.Owner) && ReferenceEquals(document.GetObjectByHandle(source.Handle), source), "Existing source membership retained");
        }
        Check(newSources[0].Reactors.Count == 1 && ReferenceEquals(newSources[0].Reactors[0], hatch), "New path source has exactly one managed occurrence");
        Check(ReferenceEquals(newSources[0].Owner, hatch.Owner), "New source is adopted into the same owner");
        string snapshot = HatchSourceSnapshot(document); var reloaded = HatchSourceLoad(HatchSourceSave(document, !binary)); Equal(snapshot, HatchSourceSnapshot(reloaded), "Inserted source paths survive alternate transport reload");
    }
}
