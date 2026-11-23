//! Dimension entity projection (`netDxf.Shadow.Dimensions`).

#![forbid(unsafe_code)]

pub mod dimension_entity_projector;
pub mod dimension_snapshot_reader;

pub use dimension_entity_projector::{DimensionEntityProjector, DimensionProjectionOptions};
pub use dimension_snapshot_reader::DimensionSnapshotReader;
