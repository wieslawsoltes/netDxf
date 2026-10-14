# Endpoint-fitted linetypes

`RenderCompileOptions.LinetypeAlignment = CadLinetypeAlignment.A` enables endpoint fitting for both signed strokes and complex symbols. The same option is available on `SceneOptions`; detached callers can set `CadStyle.LinetypeAlignment` directly. `Periodic` remains the default for source and binary compatibility. The original `StrokePattern.Apply` and `LinetypePlanner.Plan` overloads retain periodic behavior; new overloads take an explicit alignment.

In Studio, **Pattern ends** switches between **Periodic** and **Fit endpoint dashes**. The setting is workspace-local, inherited when forking, and does not mutate DXF properties or add history. Changing it advances the request epoch and clears presentation selection because glyph ordinals can change. Retained source snapshots are displayed using the currently selected view policy, like the current layout and camera; the snapshot and pinned SHX resources are not rewritten. Reloading a document restores the periodic default.

## Published open-path rule

For path length L, period P and nonnegative first dash d, let n be the largest integer with nP <= L. When n is zero, the path is drawn continuously and incomplete symbol motifs are omitted. Otherwise the first and last dash both have length (L - nP + d)/2. Interior descriptors retain their authored lengths, so both endpoint dashes are at least d/2 and gaps and secondary dashes are not stretched. A fused multiply-add and threshold correction handle division rounded across an integer repeat boundary.

The last terminal dash is also a completed first descriptor: a decoration authored on that descriptor is emitted at its completion, including the final open endpoint. Zero-length descriptors remain dots. Symbols remain whole; fitting never clips a glyph to a dash or to the path endpoint. Ordinary viewport clipping still applies.

This implements the endpoint rules in Autodesk's [About Simple Custom Linetypes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-EF1DF0A9-2088-487C-8085-16FEE6425405.htm). It is not an assertion of pixel certification against every AutoCAD release.

## Closed-path policy

The documentation does not prescribe a unique numeric closed-path residual rule. This implementation preserves every gap and secondary descriptor, distributes residual length equally over the first dash of each complete repeat, and places the seam at the center of a first dash. Start/end partial strokes therefore join continuously; duplicate seam dots and symbols are not emitted. Glyph dimensions and offsets are not stretched. A closed path shorter than one complete period uses the same solid fallback. This deterministic policy is explicit rather than claiming an undocumented proprietary algorithm.

## Shared domain and invariants

`LinetypeFitting` produces stroke intervals and symbol events from one distance-domain fit. Authored polyline partitions from `LinetypePaths` are applied first. Thus per-vertex polylines fit each authored segment while continuous polylines fit the full chain; adaptive tessellation vertices do not become fitting boundaries. Distances are sampled arc length at the requested rendering tolerance.

PSLTSCALE compensation precedes fitting and depth/viewport clipping. Phase is computed for the full source path, never for the visible clipped portion. Both native and SVG renderers consume this plan. Semantic contours, source identities, graph edges and default snapping remain unchanged; symbol selection still edits the source.

Malformed A patterns (fewer than two descriptors or a leading gap), invalid enums, nonfinite periods and unrepresentable repetition counts fail explicitly. Existing per-item and scene placement/expansion limits remain active, including terminal symbols. Positive all-dash patterns coalesce into continuous strokes. Missing fonts do not change the fitted stroke spacing.

The advanced suite covers exact and fractional repeats, short fallback, dots, closed seams, corner partitions, descriptor-attached symbols, source/cache/history isolation, viewport compensation, floating-point thresholds, 500 deterministic randomized endpoint/gap cases, Studio controls and backend output. Randomized iterations are one registered test, not 500 independently counted cases. HTTP and optional in-memory Chromium fixtures separately exercise the view control and stale-request handling.
