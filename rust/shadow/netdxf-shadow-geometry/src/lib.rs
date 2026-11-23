//! CAD geometry projection, curves, hatch patterns, and spatial indexing (`netDxf.Shadow.Geometry`).

#![forbid(unsafe_code)]

pub mod cad_appearance;
pub mod cad_component;
pub mod cad_linetype;
pub mod cad_scene;
pub mod cad_scene_builder;
pub mod cad_text_projection;
pub mod curves;
pub mod drawing_unit_conversion;
pub mod hatch_island_resolver;
pub mod hatch_pattern_generator;
pub mod projection_cache;
pub mod root_projection_cache;
pub mod spatial_index;
pub mod standard_entity_projector;

pub use cad_appearance::*;
pub use cad_component::*;
pub use cad_linetype::*;
pub use cad_scene::*;
pub use cad_scene_builder::*;
pub use cad_text_projection::*;
pub use curves::*;
pub use drawing_unit_conversion::*;
pub use hatch_island_resolver::*;
pub use hatch_pattern_generator::*;
pub use projection_cache::*;
pub use root_projection_cache::*;
pub use spatial_index::*;
pub use standard_entity_projector::*;
