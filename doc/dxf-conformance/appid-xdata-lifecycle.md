# APPID and XData lifecycle

An application registry rename updates its table index and every live XData
dictionary key. Validation runs before public name observers and again after
they return; internal indexes change only after both checks succeed. A throwing
observer or a name collision therefore leaves the original internal binding
usable. Effects performed explicitly by an observer are its own changes and
are not rolled back.

When an object joins a document, its XData is rebound to the document's canonical
registry. The caller's registry is not transferred from another document or
silently adopted. An XData value held by one dictionary retains its identity and
mutable record list, so correcting a caller-held value after failed save works.
Adding that value to another dictionary isolates the second value and binary
record arrays. Appending it to an existing application entry also isolates those
arrays while preserving the target entry and documented append behavior.

The public dictionary indexer reports removal and addition when replacing an
entry. Removing an entry detaches its name binding. Re-adding detached objects,
replacing the main layout viewport, and synchronizing INSERT attributes restore
only the current document subscriptions.

APPID reference queries count actual retained objects by identity. The scan
includes registered objects, INSERT attributes, BLOCK end records, and layout
viewports. A referenced registry cannot be removed. A foreign registry with the
same name cannot remove or impersonate the registered instance. Registries
cloned from another document receive independent XData graphs; named cloning
honors the requested name, remaps self references and cycles, and rejects a new
name that would collapse distinct entries in the cloned graph.

BLOCK and ENDBLK carry their own XData packets. The writer places each packet
after the corresponding record's own fields, and the reader restores ENDBLK
XData independently. This corrects the previous placement of BLOCK XData at the
end of ENDBLK and the omission of the latter's stored metadata. Both BLOCK clone
overloads also copy ENDBLK XData alongside BLOCK and BLOCK_RECORD metadata.
Each cloned payload and registry graph is independent, including binary arrays,
self references and cycles. Registering the cloned block rebinds all three
metadata carriers to the destination document's canonical registry.

`AppIdXDataLifecycleTests.cs` exercises six supported export profiles (R2000,
R2004, R2007, R2010, R2013 and R2018) and text/binary transports, plus mutation,
callback, sharing, cloning and removal cases. The existing STYLE fidelity tests
also cover invalid UTF-16 save failure followed by repair through the original
XData value.

`tools/verify_appid_lifecycle.py` independently opens all 40 emitted drawings
with ezdxf and checks 132 exact XData carrier packets using both the high-level
reader and physical DXF tags. It verifies APPID table uniqueness, absence of old
names, binary values, duplicate named VPORT records, main paper-space viewports,
distinct BLOCK/ENDBLK/ATTRIB payloads, and cloned BLOCK/BLOCK_RECORD/ENDBLK
payloads. Three deliberate file corruptions must
be rejected. The mandatory independent-verifier runner discovers this gate.

This qualification concerns the library's retained metadata graph and stored
DXF representation. It does not qualify arbitrary private application payloads,
native application execution, native AutoCAD behavior, or complete DXF support.
