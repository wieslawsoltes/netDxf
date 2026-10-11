using System.Collections.Immutable;

namespace netDxf.Shadow.Editor;

internal static class DimensionTranslation
{
    internal static bool TryPlan(ShadowReader entity, Vec3 delta, out ImmutableArray<ShadowPropertyEdit> changes)
    {
        string[]? coordinates = entity.Type switch
        {
            "AlignedDimension" or "LinearDimension" => ["FirstReferencePoint", "SecondReferencePoint"],
            "Angular2LineDimension" => ["StartFirstLine", "EndFirstLine", "StartSecondLine", "EndSecondLine"],
            "Angular3PointDimension" => ["CenterPoint", "StartPoint", "EndPoint"],
            "RadialDimension" or "DiametricDimension" => ["CenterPoint", "ReferencePoint"],
            "OrdinateDimension" => ["Origin", "FeaturePoint", "LeaderEndPoint"],
            "ArcLengthDimension" => ["CenterPoint"],
            _ => null
        };
        changes = [];
        if (coordinates is null) return false;
        var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
        var local = new Vec3(Vec3.Dot(delta, basis.X), Vec3.Dot(delta, basis.Y), Vec3.Dot(delta, basis.Z));
        var result = ImmutableArray.CreateBuilder<ShadowPropertyEdit>();
        foreach (var name in coordinates) Move(name);
        // Setting TextReferencePoint forces manual placement. Leave automatic references untouched;
        // the detached dimension projector regenerates them at the translated definition.
        if (entity["TextPositionManuallySet"].Boolean()) Move("TextReferencePoint");
        Add("Elevation", ShadowValue.From(entity["Elevation"].Number() + local.Z));
        changes = result.ToImmutable();
        return true;
        void Move(string property)
        {
            var before = entity[property];
            if (before.Value is not ShadowValue.Structure vector) throw new NotSupportedException("Dimension coordinates must be detached structures.");
            Add(property, vector with { Fields = vector.Fields
                .SetItem("X", ShadowValue.From(before["X"].Number() + local.X))
                .SetItem("Y", ShadowValue.From(before["Y"].Number() + local.Y)) });
        }
        void Add(string property, ShadowValue value)
        {
            if (entity.Id is not { } id || entity.Object is not { } node || !node.WritableProperties.Contains(property))
                throw new NotSupportedException("Dimension property is not writable: " + property);
            if (!ShadowValue.Equivalent(node[property], value)) result.Add(new(id, property, node[property], value));
        }
    }
}
