use netdxf_shadow::{ObjectId, ShadowSnapshot};
use netdxf_shadow_geometry::{CadGeometry, ICacheableEntityProjector, IEntityProjector};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DimensionProjectionOptions {
    pub maximum_source_objects: usize,
    pub maximum_source_values: usize,
    pub maximum_depth: usize,
    pub maximum_generated_occurrences: usize,
    pub maximum_text_length: usize,
    pub culture_name: String,
    pub regenerate_missing_blocks: bool,
}

impl Default for DimensionProjectionOptions {
    fn default() -> Self {
        Self {
            maximum_source_objects: 4_096,
            maximum_source_values: 131_072,
            maximum_depth: 16,
            maximum_generated_occurrences: 16_384,
            maximum_text_length: 1_024,
            culture_name: "en-US".into(),
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

    #[inline]
    pub fn revision(&self) -> i64 {
        0
    }

    pub fn try_project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        self.project(snapshot, entity_id)
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
