using System.Collections.Immutable;
using netDxf.Shadow.Modules;

namespace netDxf.Shadow.Disciplines;

public static class DomainCommands
{
    public static CadModuleField Field(string id, string name, string type = "text", bool required = false, string value = "", params string[] choices)
        => new(id, name, type, required, value, choices.ToImmutableArray());
    public static ImmutableArray<CadModuleCommand> Create(string routeName, ImmutableArray<CadModuleField> routeFields, ImmutableArray<CadModuleField> updateFields)
        => [new("place", "Place symbol", "Place a versioned native block with attributes and declared ports.", 1, 1, RequiresSymbol: true,
                Fields: [Field("tag", "Tag", required: true), Field("rotation", "Rotation", "number", value: "0"), Field("scale", "Uniform scale", "number", value: "1")]),
            new("route", routeName, "Create tagged segments with explicit intermediate endpoint connections.", 2, 64, Fields: routeFields),
            new("update", "Update data", "Update metadata and matching native attributes in one undoable transaction.", RequiresSelection: true, Fields: updateFields),
            new("connect", "Connect ports", "Record reciprocal references between two selected domain objects.", RequiresSelection: true,
                Fields: [Field("sourcePort", "Source port", required: true), Field("targetPort", "Target port", required: true), Field("role", "Relation", "select", value: "connection", choices: ["connection", "association"])])];
}
