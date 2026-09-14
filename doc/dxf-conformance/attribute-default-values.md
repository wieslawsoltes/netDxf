# Attribute default values

A new `AttributeDefinition("TAG")` has an empty string value. All constructor overloads use that same default. `Attribute` copies the definition value through the existing value-normalization rule, and newly synchronized INSERT attributes inherit the empty default.

Both public `Value` setters already convert `null` to `string.Empty`. `AttributeDefinition.Prompt` now follows that same rule, consistent with its existing constructor and reader defaults. Empty text is valid; whitespace and nonempty text remain unchanged. The ATTDEF and ATTRIB readers also already initialize a missing group 1 to an empty string. This fix makes construction consistent with those existing setter and reader semantics. Saving emits an explicit empty group 1 for a value and group 3 for a prompt; it does not introduce a distinction between an omitted field and empty text.

Previously, the definition constructor assigned `null` directly, and INSERT construction copied that null into ATTRIB. Both writers then passed null to the group-value writer, causing a Debug assertion failure. Assigning null to `Prompt` caused the same failure for group 3. Default construction, explicit null assignment, explicit empty values, whitespace, nonempty text, cloning, INSERT synchronization, and omitted input fields are covered across all six supported profiles and both transports.

The `(string tag, TextStyle style)` constructor also validates a null style before reading its height and reports `ArgumentNullException` with parameter `style`, consistent with the explicit-height constructor. A valid fixed style height remains inherited; a style height of zero still selects attribute height 1. An explicit attribute height remains unchanged.

The focused registration is `RegisterAttributeDefaultValueTests()` in `AttributeDefaultValueTests.cs` (25 scenarios). Independent scratch reproduction established both ATTDEF and ATTRIB null write failures separately in text and binary output before the fix.
