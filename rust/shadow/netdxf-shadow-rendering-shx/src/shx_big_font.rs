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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxLeadByteRange {
    pub start: u8,
    pub end: u8,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxEncodedCharacter {
    pub code: u32,
    pub byte_length: u8,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxBigFontInfo {
    pub lead_ranges: Vec<ShxLeadByteRange>,
    pub height: u8,
    pub width: u8,
}

#[derive(Debug, Clone, Default)]
pub struct ShxBigFontMap {
    pub unicode_to_code: BTreeMap<char, u32>,
}

