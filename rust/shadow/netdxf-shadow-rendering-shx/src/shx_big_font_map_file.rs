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
pub struct ShxBigFontMapReadOptions {
    pub maximum_entries: usize,
}

pub struct ShxBigFontMapFile;

impl ShxBigFontMapFile {
    /// Parses a simple `hex_code=U+XXXX` bigfont mapping table.
    pub fn parse(text: &str, options: &ShxBigFontMapReadOptions) -> ShxBigFontMap {
        let mut map = ShxBigFontMap::default();
        let limit = if options.maximum_entries == 0 {
            usize::MAX
        } else {
            options.maximum_entries
        };
        for line in text.lines() {
            if map.unicode_to_code.len() >= limit {
                break;
            }
            let trimmed = line.trim();
            if trimmed.is_empty() || trimmed.starts_with('#') || trimmed.starts_with(';') {
                continue;
            }
            if let Some((lhs, rhs)) = trimmed.split_once('=') {
                let code_str = lhs.trim().trim_start_matches("0x").trim_start_matches("0X");
                let uni_str = rhs.trim().trim_start_matches("U+").trim_start_matches("u+");
                if let (Ok(code), Ok(cp)) = (
                    u32::from_str_radix(code_str, 16),
                    u32::from_str_radix(uni_str, 16),
                ) {
                    if let Some(ch) = char::from_u32(cp) {
                        map.unicode_to_code.insert(ch, code);
                    }
                }
            }
        }
        map
    }
}

