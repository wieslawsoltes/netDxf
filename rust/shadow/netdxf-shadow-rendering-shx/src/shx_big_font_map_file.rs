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
    pub fn read(text: &str, options: &ShxBigFontMapReadOptions) -> ShxBigFontMap {
        Self::parse(text, options)
    }

    pub fn read_strict(text: &str, options: &ShxBigFontMapReadOptions) -> Result<ShxBigFontMap, String> {
        let mut map = ShxBigFontMap::default();
        let limit = if options.maximum_entries == 0 {
            usize::MAX
        } else {
            options.maximum_entries
        };
        for (line_no, line) in text.lines().enumerate() {
            let trimmed = line.trim();
            if trimmed.is_empty() || trimmed.starts_with('#') || trimmed.starts_with(';') {
                continue;
            }
            if map.unicode_to_code.len() >= limit {
                return Err(format!("BigFont map exceeds maximum entry limit ({limit})."));
            }
            let Some((lhs, rhs)) = trimmed.split_once('=') else {
                return Err(format!("Invalid mapping at line {}.", line_no + 1));
            };
            let code_str = lhs.trim().trim_start_matches("0x").trim_start_matches("0X");
            let uni_str = rhs.trim().trim_start_matches("U+").trim_start_matches("u+");
            let code = u32::from_str_radix(code_str, 16)
                .map_err(|_| format!("Invalid code at line {}.", line_no + 1))?;
            let cp = u32::from_str_radix(uni_str, 16)
                .map_err(|_| format!("Invalid Unicode scalar at line {}.", line_no + 1))?;
            let ch = char::from_u32(cp)
                .ok_or_else(|| format!("Invalid Unicode char at line {}.", line_no + 1))?;
            map.unicode_to_code.insert(ch, code);
        }
        Ok(map)
    }

    pub fn write(map: &ShxBigFontMap) -> String {
        let mut out = String::new();
        for (&ch, &code) in &map.unicode_to_code {
            out.push_str(&format!("0x{code:04X}=U+{:04X}\n", ch as u32));
        }
        out
    }

    pub fn map(map: &ShxBigFontMap, ch: char) -> Option<u32> {
        map.unicode_to_code.get(&ch).copied()
    }

    pub fn equivalent(a: &ShxBigFontMap, b: &ShxBigFontMap) -> bool {
        a.unicode_to_code == b.unicode_to_code
    }

    pub fn resource(name: &str) -> String {
        format!("shx-bigfont-map:{name}")
    }

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

