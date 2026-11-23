//! Immutable reactive CAD object-graph foundation (`netDxf.Shadow`).

#![forbid(unsafe_code)]

pub mod math;
pub mod shadow_graph_patch;
pub mod shadow_merge;
pub mod shadow_read_set;
pub mod shadow_snapshot;
pub mod shadow_value;

pub use math::{Affine3, Bounds2, Vec3};
pub use shadow_graph_patch::{
    IShadowGraphStore, ShadowGraphEdit, ShadowGraphPatch, ShadowObjectChange,
};
pub use shadow_merge::{
    ShadowMerge, ShadowMergeChoice, ShadowMergeConflict, ShadowMergeConflictKind,
    ShadowMergeOptions, ShadowMergePlan,
};
pub use shadow_read_set::{IShadowReadObserver, ShadowDependency, ShadowReadSet};
pub use shadow_snapshot::{
    IShadowStore, ShadowConflictException, ShadowDelta, ShadowEditBatch, ShadowMemoryStore,
    ShadowPropertyEdit, ShadowReader, ShadowResult, ShadowSnapshot,
};
pub use shadow_value::{
    Atom, DiagnosticSeverity, Fault, NullValue, ObjectId, Reference, Sequence, ShadowDiagnostic,
    ShadowObject, ShadowObjectKind, ShadowValue, Structure,
};
