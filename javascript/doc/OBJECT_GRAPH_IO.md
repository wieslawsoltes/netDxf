# OBJECTS graph integration

Typed document loading/saving is integrated. The OBJECTS partials coordinate
payload codecs, physical source identity, registered ownership and output; they
are not substitutes for the main reader/writer's other section and resource phases.

## Import and source identity

`DxfReader.Objects.js` implements ReadDatabaseRecord,
ReadDictionaryDatabaseRecord, ReadXRecordDatabaseRecord, ApplyDatabaseMetadata and
ImportDatabaseObjects. Its DatabaseIOContext supplies physical record observations
and the legacy state prepared by the main reader.

Recognized schemas use their qualified payload codecs. Unsupported private data
remains opaque rather than being partially reinterpreted. A control-looking tag
inside an XRECORD payload is not automatically object-header metadata. Capture
physical source identity before advancing; a generated runtime default must not
become a replacement for a missing, discarded or ambiguous source object.

Import coordinates handle reservations, named-root replacement, preserved managed
collection identities, owners, dictionary entries/defaults, extensions, reactors
and deferred resolver phases. Repeated references and aliases have counted,
identity-sensitive behavior. A failing import can expose partially completed
state through callbacks; it must not return a falsely successful document.

Dictionary/physical metadata maps and legacy DictionaryObject entry maps have
different comparison rules. The legacy DictionaryObject, XRecord and XRecordEntry
models are not interchangeable with immutable raw tags or registered-object
collections. Keep those adapters and their tests even when their names overlap.

## Output and retained data

`DxfWriter.Objects.js` implements metadata, object envelopes, payload dispatch,
text preflight, database-string encoding and CLASS preparation. Registered objects
supply canonical identities. Extensions precede ordered, case-insensitively
deduplicated reactor output. Legacy generated names and typed entry names retain
their separate escaping policies.

Unknown private bodies remain opaque. FIELD source validation is not bypassed
by binary transport's different string handling. CLASS declarations retain the
source's compatibility and instance-count rules. Callback-time version/property
reads and completed output prefixes remain observable on failure.

TABLE/content/geometry/style, FIELD, DIMASSOC, SUNSTUDY, SECTION_MANAGER and related
payload families have source-specific ownership and stored-data contracts in
their original-path implementations and test corpora. Opaque retention and
editable projections do not establish full evaluator, layout, CAD rendering or
newer-version semantics. Do not delete helper codecs because the main file is
only a dispatcher.

## Qualification and open conditions

Run `test:object-graph-io`, relevant original modules, supplemental tests and
package checks. The complete differential aggregate and browser lanes are
separate. Use [VERIFICATION.md](VERIFICATION.md) for setup and result locations.

The historical Debug case
`object-graph-io/metadata/automatic/["C0",null]` aborted in native
TextCodeValueWriter while writing an automatic-reactor list. It remains an
unavailable native observation until reproduced and resolved; it is not a passing
comparison, a permitted skip, or a reason to abort the JavaScript host to imitate
Debug.Assert. Preserve the corpus and report missing operations explicitly.

Full native API, graph, wire and failure-order qualification remains incomplete.
Current coverage is kept in the [README](../README.md), not in checkpoint tables
repeated across implementation guides.
