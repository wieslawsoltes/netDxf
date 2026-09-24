# IMAGE appearance edits and cached graphics

The IMAGE model stores rendering settings separately from common proxy graphics.
Brightness, Contrast, Fade, DisplayOptions and Clipping now invalidate that
cache when their stored value changes. Assigning the same value preserves it.
The original 0-100 admission for the three scalar settings is unchanged; a
rejected assignment leaves the value and cache unchanged. Absent and explicitly
empty graphics remain distinct.

ClippingBoundary replacement invalidates the cache when the stored object
reference changes. Assigning the current boundary is a no-op. Assigning null
retains the existing full-image reset: a new rectangle is made from the current
definition's pixel size, then published and the old cache removed. Even a null
reset of an already full-image boundary creates a new object, as before.
Construction/validation precedes assignment, and no setter changes image
position, dimensions, axes, definition, handles or XData.

The reader/writer and their hydration sequence are unchanged. Loading stored
appearance is not a user edit: existing common proxy bytes survive complete
typed load and save/reload, then subsequent public appearance edits invalidate
normally. Image definition canonicalization is deliberately left unchanged.
Clone's existing final common-data copy retains the original cache in an
independently editable clone.

## Verification

The focused harness has 139 cases: 18 scalar/reference no-ops across three
cache states, 21 changes with clone isolation, 36 out-of-range refusals,
15 accepted endpoints/interior values, one repeated default-boundary reset,
and 48 document matrices. The matrices generate 144 drawings / 3,456 IMAGE
records across six supported typed profiles, both transports and modelspace,
paper layout, referenced block and unreferenced block placement. Handles,
definition identity, geometry, pixel clipping, XData and following LINE are
checked through both output transports. The loaded unchanged sample is edited
again to distinguish hydration from subsequent API mutation.

The independent checker derives expected appearance and pixel coordinates from
the specified fixture inputs. It checks exact scalar/ordered vertex fields and
ordered common proxy packets, including profile-dependent length codes and
explicit zero-length packets. ezdxf maps an empty packet to None, so that
distinction is checked physically rather than inferred from its object model.
The checker also verifies resolved IMAGEDEF, ownership, placement, XData and
audit results. Every field mutation, missing/extra file and stale proxy insertion
must reject. Executed totals and comparisons are recorded in the PR rather
than inferred from test definitions.

The shared installed-package body adds all six property changes, no-op
assignments and refused-brightness/empty-cache checks to the existing twelve
round trips. The ordinary consumer and eight exact-assembly/runtime profiles
reuse that body. The two consolidated workflows are unchanged.

## Boundaries

This clears a stale cache; it does not decode images or produce a replacement
raster/native proxy. Definition replacement and changes within a referenced
definition or a cast-mutated clipping vertex list are not newly observed.
Common entity appearance properties, event transactionality, arbitrary
malformed IMAGE input, inverted clipping, and historic dialect support are
outside this increment. No reader/writer or public signature changes are made.
Synthetic packets and independent reads are not native AutoCAD visual acceptance.

Primary references:
- [Autodesk IMAGE fields](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3A2FF847-BE14-4AC5-9BD4-BD3DCAEF2281.htm): display flags, clipping, brightness/contrast/fade and pixel boundaries.
- [Autodesk common entity fields](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): common proxy length and binary payload.
The invalidation policy is a library editing contract, not an Autodesk-specified
algorithm for constructing or invalidating graphics.
