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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxBigFontBinding {
    pub font_name: String,
    pub code_page: String,
}

#[derive(Debug, Clone, Default)]
pub struct ShxFontSet {
    pub fonts: BTreeMap<String, ShxFont>,
}

#[derive(Debug, Clone, Default)]
pub struct ShxFontCatalog {
    pub set: ShxFontSet,
    pub big_font_bindings: Vec<ShxBigFontBinding>,
}

