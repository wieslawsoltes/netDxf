# Typed DxfObject / XData lifecycle

## Source and recovery contract

This increment ports the pinned `DxfObject`, `DxfObjectReference`, `XData`, `Collections/XDataDictionary`, `Tables/TableObject`, `Tables/TableObjectChangedEventArgs`, and both `Tables/ApplicationRegistry` partial source files at the original paths. It does not turn raw records into a replacement for the not-yet-ported typed document engine.

Before this increment, the remote branch had already reconciled the saved foundations work. The retained `recovery/foundations-fb10b22.patch.xz` decompresses to SHA-256 `7773e68f1eadcb236be68544e0bf1043edd3fd8679cb9c937db8f56549d7eef1`, identical to the original saved cumulative patch. The newer generated geometry remains canonical; no prior source or recovery data is discarded here.

## Semantics

DxfObject forwards registry-add/remove notifications with multicast snapshot ordering and preserves nullable handle/owner/extension metadata and the persistent reactor list. Internal Int64 handle assignment uses BigInt, including unchecked wraparound and two's-complement hexadecimal formatting.

XData retains the caller's live record list. Public cloning dispatches the registry's public Clone override; stored metadata copying deliberately bypasses that override. Binary record buffers are copied independently. Sharing an XData value across dictionaries makes a private stored graph copy; merging a foreign value into an existing name copies its records against the existing registry. Same-container self-merging retains the source List.AddRange(self) behavior.

Registry renaming validates all attached dictionaries, notifies public observers before changing the name, then revalidates the current binding set. An observer can throw or create a collision without prematurely changing the registry name or the old binding index. Observer side effects themselves are not rolled back. NameChanged.NewValue mutation is ignored by the pinned source's setter and remains ignored here. Constructor trimming, setter whitespace, and reserved-name checks preserve the source's differences rather than silently repairing them.

Application-registry graph cloning preserves cycles and aliases through an identity map. The port uses an explicit depth-first stack, not recursive native calls; a 12,000-registry cycle is covered by a supplemental test. Each original record is independently cloned; mutable binary chunks do not leak across source and copy graphs.

The string dictionary adapter uses the pinned ordinal key function, live read-only key/value views, .NET 8 slot order and free-slot reuse. Adding a new entry invalidates active enumerators; removing or clearing entries does not. This targets the actual pinned .NET runtime, not every legacy CLR. See Microsoft's [dictionary enumerator documentation](https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.keycollection.enumerator.system-collections-ienumerator-reset) and the executable lifecycle corpus for observed behavior.

## JavaScript adaptation

Public names remain PascalCase. Indexers use get_Item/set_Item; out parameters use `{ value: ... }`; events use Add/Remove. Explicit dictionary interface members use Add(key, value), AddPair, RemovePair, Contains and CopyTo. The original IDictionary.Add key is deliberately ignored by XDataDictionary; the indexer, by contrast, validates the key against the registry name.

The XDataRecord and PersistentReactors lists are reference-element runtime adapters with Count, Add/AddRange, Clear, Insert, Remove/RemoveAt, Contains/IndexOf, get_Item/set_Item, ToArray, CopyTo, and versioned GetEnumerator. They are iterable, not JavaScript arrays. This does not implement every BCL List overload.

C# internal/protected setters and helpers remain callable for future in-repository engine composition. Consumers must not assign Container, ApplicationRegistry, Owner, Handle or binding helpers directly to bypass ownership rules. Such assignments are not new public authoring APIs. ApplicationRegistry.Clone without arguments delegates to the named overload through normal JavaScript override dispatch; a subclass implementing both C# overloads must dispatch its own Clone by argument count.

TableObject.Equals defaults to the C# object overload (same concrete type); EqualsTableObject is the explicitly selected table-object overload. GetHashCode preserves a case-sensitive per-name hash but does not reproduce .NET's randomized per-process numeric string hash. Hash outputs, arbitrary subclass equality, full culture-dependent ordering, all generic/BCL operations, and registered document ownership are not qualified by this increment.

## Verification

Seven additional original test identities are registered without substituting a raw object for a typed entity: the five standalone XData clone cases, the APPID cyclic named-clone case, and the public metadata-clone override case. Their source filenames and method names are retained. The document/entity/layer/block cases in those same source files remain missing from the completion ledger.

`npm run test:lifecycle` compares 523 scripted scenarios and 15,563 operations with the actual pinned .NET production classes. Snapshots contain the complete reachable reference graph (including record/byte-array aliases), event order, exception class and parameter, dictionary slots, handles, rename effects, observer failures, binding replacements, and clone identities. The 128 emission scenarios compare 256 text/binary raw-envelope outputs authored from typed XData; they do not claim typed DxfDocument serialization parity. No numeric tolerance, renamed expected result, or ignored failure is used.

The aggregate verifier requires fresh lifecycle evidence. The real-browser corpus also includes all 523 scenarios. Local Node success is not browser qualification; CI must execute the browser. Existing exact geometry, globalization-host and Windows held-reader failures remain separate blocking categories. The full-port completion gate is unchanged.
