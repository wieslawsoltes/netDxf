# Spatial linetype transport

Set `LinetypePlanOptions.FrameMode = LinetypeFrameMode.ParallelTransport` when constructing `RenderCompiler` or calling `LinetypePlanner.Plan` to enable complex symbols on nonplanar sampled paths. The default `AuthoredPlane` mode preserves existing output and explicit nonplanar refusal.

`LinetypeTransport.Create` produces immutable rotation-minimizing edge frames. Distances and pattern-event scheduling still use the shared periodic/A-fitting engine. Authored polyline boundaries restart paths before transport. Duplicate vertices consume no length or rotation. A least-aligned-axis seed handles an authored normal parallel to the initial tangent. Minimal tangent-to-tangent rotations transport the side vector, followed by reorthogonalization; at an antiparallel cusp, the previous side is the deterministic half-turn axis.

Closed paths optionally distribute residual seam roll by sampled arc length (`CloseFrameSeam`, enabled by default). This makes the transported seam orientation close without imposing a discontinuous twist at one point. The policy is deterministic, not a claim of a unique frame at a geometric cusp or an exact analytic Frenet frame. At an exact vertex the incoming edge defines the glyph frame, matching existing placement semantics.

Relative glyph orientation follows transported tangent/side vectors. Absolute and upright orientations use the local normal's object-coordinate basis. Offsets remain tangent/side-relative. Both render backends consume the same generated geometry. Source identities, presentation flags, picking and semantic topology remain unchanged.

Frames are bounded by the point quota and cancellation checks. Zero-length paths, nonfinite coordinates, invalid seed normals and unrepresentable accumulated distances are rejected before output. Curved paths use the existing sampling tolerance; tighter tolerances improve the polyline approximation, not an analytic certification.

The executable verification covers helix continuity, finite orthonormality, planar equivalence, cusps, duplicate points, closed explicit/implicit seams, fitting, rendering, budgets, cancellation and legacy behavior.
