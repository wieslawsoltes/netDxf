#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadContour, CadGeometry, ICacheableEntityProjector, IEntityProjector};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxProjectionOptions {
    pub fallback_to_ttf: bool,
    pub maximum_cached_glyphs_per_font: usize,
    pub maximum_characters: usize,
    pub maximum_curves: usize,
    pub maximum_glyph_instructions: usize,
}

#[derive(Debug, Clone, Default)]
pub struct ShxEntityProjector {
    pub catalog: ShxFontCatalog,
    pub options: ShxProjectionOptions,
}

impl IEntityProjector for ShxEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        if matches!(reader.type_name(), "Text" | "MText" | "Shape") {
            Some(CadGeometry::default())
        } else {
            None
        }
    }
}

impl ICacheableEntityProjector for ShxEntityProjector {
    fn projector_key(&self) -> &str {
        "shx"
    }

    fn revision(&self) -> i64 {
        self.catalog.set.revision
    }
}

impl ShxEntityProjector {
    pub fn try_project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        self.project(snapshot, entity_id)
    }
}

