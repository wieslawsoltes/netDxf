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
    pub lead_byte_ranges: Vec<ShxLeadByteRange>,
    pub height: u8,
    pub width: u8,
    pub character_width: u8,
    pub approximate_characters: bool,
}

impl ShxBigFontInfo {
    pub fn is_lead_byte(&self, byte: u8) -> bool {
        self.lead_ranges
            .iter()
            .chain(self.lead_byte_ranges.iter())
            .any(|r| byte >= r.start && byte <= r.end)
    }

    pub fn decode(&self, bytes: &[u8]) -> Vec<ShxEncodedCharacter> {
        let mut out = Vec::new();
        let mut i = 0;
        while i < bytes.len() {
            let b = bytes[i];
            if self.is_lead_byte(b) && i + 1 < bytes.len() {
                let code = ((b as u32) << 8) | (bytes[i + 1] as u32);
                out.push(ShxEncodedCharacter {
                    code,
                    byte_length: 2,
                });
                i += 2;
            } else {
                out.push(ShxEncodedCharacter {
                    code: b as u32,
                    byte_length: 1,
                });
                i += 1;
            }
        }
        out
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShxBigFontMap {
    pub unicode_to_code: BTreeMap<char, u32>,
}

