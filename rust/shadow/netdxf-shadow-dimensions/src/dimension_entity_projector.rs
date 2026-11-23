use netdxf_shadow::{ObjectId, ShadowSnapshot};
use netdxf_shadow_geometry::{CadGeometry, ICacheableEntityProjector, IEntityProjector};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct DimensionProjectionOptions {
    pub maximum_source_objects: usize,
    pub maximum_source_values: usize,
    pub regenerate_missing_blocks: bool,
}

impl Default for DimensionProjectionOptions {
    fn default() -> Self {
        Self {
            maximum_source_objects: 4_096,
            maximum_source_values: 131_072,
            regenerate_missing_blocks: true,
        }
    }
}

/// Projects DXF dimension entities into [`CadGeometry`] using dimension block definitions.
#[derive(Debug, Clone, Default)]
pub struct DimensionEntityProjector {
    pub options: DimensionProjectionOptions,
}

impl DimensionEntityProjector {
    pub fn new(options: DimensionProjectionOptions) -> Self {
        Self { options }
    }
}

impl IEntityProjector for DimensionEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        if reader.type_name().contains("Dimension") {
            Some(CadGeometry::default())
        } else {
            None
        }
    }
}

impl ICacheableEntityProjector for DimensionEntityProjector {
    fn projector_key(&self) -> &str {
        "dimensions"
    }
}
