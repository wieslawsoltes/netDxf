using System.Text.Json;
using netDxf.Shadow.Studio;

internal static class StudioTransformVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("sample/rotate scale and mirror enter canonical undo history", () =>
        {
            using var studio = new Studio();
            JsonElement State() => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var initial = State(); var item = initial.GetProperty("items").EnumerateArray().First(i => i.GetProperty("type").GetString() == "Line");
            studio.Select(new(initial.GetProperty("revision").GetInt64(), item.GetProperty("key").GetString()!));
            foreach (var (operation, amount) in new[] { ("rotate", 90.0), ("scale", 2.0), ("mirror-x", 1.0) })
            {
                var state = State(); var count = state.GetProperty("undoCount").GetInt32();
                studio.Transform(new(state.GetProperty("revision").GetInt64(), operation, amount));
                var changed = State(); Require(changed.GetProperty("undoCount").GetInt32() == count + 1);
                studio.History("undo", changed.GetProperty("revision").GetInt64());
                Require(State().GetProperty("redoCount").GetInt32() == 1);
            }
        }));
        tests.Add(("sample/dimension fragments select and translate the parent entity", () =>
        {
            using var studio = new Studio();
            JsonElement State() => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            studio.SetLayout(new(State().GetProperty("revision").GetInt64(), "Dimensions"));
            var first = State(); var item = first.GetProperty("items").EnumerateArray().First(i => i.GetProperty("type").GetString() == "AlignedDimension");
            studio.Select(new(first.GetProperty("revision").GetInt64(), item.GetProperty("key").GetString()!));
            studio.Translate(new(first.GetProperty("revision").GetInt64(), 10, 5));
            Require(State().GetProperty("undoCount").GetInt32() == 1);
            Require(State().GetProperty("diagnostics").EnumerateArray().All(d => d.GetProperty("severity").GetString() != "Error"));
            studio.History("undo", State().GetProperty("revision").GetInt64());
            Require(State().GetProperty("occurrences").GetInt32() == first.GetProperty("occurrences").GetInt32());
        }));
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Studio transform regression."); }
}
