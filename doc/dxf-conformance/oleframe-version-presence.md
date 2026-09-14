# Legacy OLEFRAME optional version presence — local refinement

Base: merged PR #81, commit `a8fb9e3bbe57e304b0471b6fa64f8fb5da24a232`, tree `32180a951d6d14f3fcaf80f81345036de4fc05a2`.

The existing reader accepts an absent group-70 version and exposes its historical default of 1. Before this correction, saving or cloning that frame always introduced an explicit version tag. An omitted field and an explicitly stored default were therefore indistinguishable on output. This correction preserves the distinction without changing the published constructor or the `short OleVersion` getter.

`HasOleVersion` reports stored presence. Newly constructed frames continue to include the version by default. `WithOleVersionPresence(bool)` returns an independently copied immutable frame selecting presence, without database identity or source mutation. `Clone` preserves presence, including through nested INSERT copies and identity-only explosion. The reader passes its existing `versionSeen` state to the model; the writer conditionally emits only group 70.

This is metadata selection, not private OLE editing. Choosing absence leaves the authored value dormant in memory but deliberately omits it from the file; after reloading, the getter uses its established default of 1. The required group-1 `OLE` terminator, exact byte-length validation, 127-byte chunk limit, finite interpretation policies, unknown-extension rejection and exact-identity-only transformation contract are unchanged. This does not adopt the earlier alternative nullable-version API or its permissive missing-terminator policy.

```csharp
var original = new OleFrame(bytes, oleVersion: 7);
var omitted = original.WithOleVersionPresence(false);
// original.HasOleVersion == true; omitted.HasOleVersion == false.
// omitted.OleVersion remains 7 in memory, but that value is not serialized.
var restored = omitted.WithOleVersionPresence(true);
// restored writes the dormant value 7; neither source object changes.
```

The 96 independently encoded wire cases cover all six admitted typed profiles and both transports, explicit/absent versions, values 0/1/7/32767, three alternating-format cycles, clones, payload identity, XData and a following LINE. The unchanged PR #81 production fails 48 absent-version cases; the other 48 explicit-version controls pass. Four direct API cases cover each authored value, selection/restoration, source and byte isolation, nested clones, identity explosion and failed transform nonmutation.

The wire cases were executed jointly with the OLE2FRAME optional-metadata regression against the entirely unchanged PR #81 production: **17,864 passed / 804 failed in Debug and Release**. Of those failures, 48 belong to this legacy version correction and 756 to OLE2FRAME. The joint red run excludes the 71 new API cases because the old assembly does not expose those APIs. Final combined counts are recorded in the delivery checkpoint; no false old-assembly API red run is claimed.

`verify_ole_version.py` independently reads 24 exported drawings / 48 packets using ezdxf 1.4.4 generic ordered tag storage and checks exact presence, payload bytes, required terminator and neighboring data. The first verifier run attempted a semantic `color` getter on ezdxf generic storage; it was corrected to inspect the exact AcDbEntity group-62 tag, without changing expected values or production. Synthetic bytes are not certified native OLE objects. Graph AUDIT is not native OLE validation or rendering. No new historical typed admission, Windows/SDK/netstandard2.0 execution or native AutoCAD execution is asserted for this local refinement.

Primary packet reference: [Autodesk OLEFRAME](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-4A10EF68-35A3-4961-8B15-1222ECE5E8C6.htm). Input permissiveness for an omitted version is an existing netDxf policy, not an assertion that Autodesk permits omission in every producer/version.
