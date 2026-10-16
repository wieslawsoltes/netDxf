using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.NetDxf;

internal static class CollectionOrderingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (bool tableFirst in new[] { false, true })
        foreach (bool keepUnrelated in new[] { false, true })
            tests.Add(($"structure/block history tableFirst={tableFirst} unrelated={keepUnrelated}", () =>
            {
                var document = new DxfDocument();
                var original = new Line(new Vector3(10, 20, 0), new Vector3(40, 20, 0));
                document.Entities.Add(original);
                var unrelated = new Block("Untouched");
                unrelated.Entities.Add(new Circle(Vector3.Zero, 3));
                var otherInsert = new Insert(unrelated, new Vector3(100, 0, 0));
                if (keepUnrelated) document.Entities.Add(otherInsert);
                var source = new ShadowSession(document);
                var before = source.Current;
                var id = before.DrawingEntities().Single(e => e.Type == "Line").Id!.Value;
                var plan = new DraftingPlanner().PlanBlockDefinition(before, new("Created", new(10, 20), [id]));
                var after = source.Apply(Order(plan.Patch, tableFirst));
                var definition = document.Blocks["Created"];
                var inserted = document.Entities.Inserts.Single(i => ReferenceEquals(i.Block, definition));
                for (int cycle = 0; cycle < 8; cycle++)
                {
                    source.Apply(Order(ShadowGraphPatch.Between(source.Current, before), tableFirst));
                    Require(!document.Blocks.Contains("Created"), "Undo left a referenced definition in the native table.");
                    Require(ReferenceEquals(document.Entities.Lines.Single(), original), "Undo replaced source identity.");
                    Require(document.Entities.All.Count() == (keepUnrelated ? 2 : 1), "Undo changed unrelated roots.");
                    VerifyUnrelated();
                    source.Apply(Order(ShadowGraphPatch.Between(source.Current, after), !tableFirst));
                    Require(ReferenceEquals(document.Blocks["Created"], definition), "Redo replaced definition identity.");
                    Require(document.Entities.Inserts.Any(i => ReferenceEquals(i, inserted)), "Redo replaced INSERT identity.");
                    Require(!document.Entities.Lines.Any(), "Redo did not restore converted membership.");
                    VerifyUnrelated();
                    var current = source.Current;
                    Require(ReferenceEquals(source.Refresh(), current), "No-op source refresh was not stable.");
                }

                void VerifyUnrelated()
                {
                    if (!keepUnrelated) return;
                    Require(ReferenceEquals(document.Blocks["Untouched"], unrelated), "Unrelated definition was replaced.");
                    Require(document.Entities.Inserts.Any(i => ReferenceEquals(i, otherInsert)), "Unrelated INSERT was removed.");
                    Require(ReferenceEquals(otherInsert.Block, unrelated) && unrelated.Entities.Single() is Circle,
                        "Unrelated block contents or references changed.");
                }
            }));
    }

    private static ShadowGraphPatch Order(ShadowGraphPatch patch, bool tableFirst) => patch with
    {
        Changes = patch.Changes.OrderBy(c =>
                ((c.After ?? c.Before)?.TypeName == "netDxf.Collections.BlockRecords") == tableFirst ? 0 : 1)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).ToImmutableArray()
    };

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
