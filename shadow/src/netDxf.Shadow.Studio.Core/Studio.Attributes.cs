using System.Collections.Immutable;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Studio;

public sealed record CadAttributeRequest(long Revision, ObjectId Insert, ImmutableArray<AttributeEdit> Attributes);

public sealed partial class Studio
{
    public object CadReadAttributes(long revision)
    {
        lock (gate)
        {
            Check(revision); var selected = CadSelected(Scene());
            if (selected.Length != 1 || Current.Read(selected[0]).Type != "Insert")
                throw new ArgumentException("Select one block reference to edit its attributes.");
            var root = Current.Read(selected[0]);
            return new { revision = Current.Revision, insert = selected[0], blockName = root["Block"]["Name"].Text,
                drawingVersion = Current.Document["DrawingVariables"]["AcadVer"].Text,
                attributes = DraftingAttributes.Read(Current, selected[0]) };
        }
    }

    public object CadEditAttributes(CadAttributeRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); var selected = CadSelected(Scene());
            if (selected.Length != 1 || selected[0] != request.Insert)
                throw new ShadowConflictException("The selected block reference changed. Reopen the attribute editor.");
            return ApplyAuthoring(DraftingAttributes.Plan(Current, request.Insert, request.Attributes, token), selected,
                "attributes", request);
        }
    }
}
