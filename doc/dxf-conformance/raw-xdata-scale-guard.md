# Guard geometry-dependent XData scalars during raw edits

`WithLineEndpoints` now rejects actual changes when the selected LINE has
XData distance (1041) or scale-factor (1042) values. The previous guard rejected
coordinate and handle XData but missed these two scalar categories. Scaling
both endpoints could therefore retain stale application-defined distance data.

The entity-neutral XData predicate is shared with the CIRCLE/ARC editor, whose
existing admission behavior is unchanged. Ordinary real (1040) data remains
preserved and editable. All finite distance/scale values, including zero and
negative values, receive the same conservative admission rule. There is no
claim that every endpoint edit corresponds to a unique affine scale: the API
rejects instead of guessing how application-defined scalars should change.

Read-only geometry access and bit-identical no-op edits remain supported.
Rejected changes preserve the source snapshot and its original bytes. This
correction does not regenerate XData, rewrite unrelated records, or change the
generic raw parser. Existing private/coordinate/handle edit guards stay enabled.

## Evidence

The same 162 cases run before and after the production change. They cover nine
raw families, text/binary input, three scalar categories and three values.
Before: 54 valid-control passes and 108 failures. After: all 162 pass. Every case
checks read access, no-op snapshot identity, byte preservation and original
endpoints. Valid 1040 cases also check unchanged tag objects and order.

```sh
DXF_TEST_FILTER=raw-xdata-scale/ dotnet run --project tests/netDxf.Conformance -c Release
```

Autodesk's [extended-data reference](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-DXF/files/GUID-A2A628B0-3699-4740-A215-C560E7242F63.htm)
defines 1041 and 1042 as values scaled with their owning entity. The conservative
rejection policy is a library contract, not a prescribed AutoCAD exception.
This does not add historical typed loading, dependency-complete import,
private-cache regeneration or native AutoCAD execution evidence.
