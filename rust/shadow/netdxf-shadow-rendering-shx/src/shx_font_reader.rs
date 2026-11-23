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

pub struct ShxFontReader;

impl ShxFontReader {
    pub fn read(
        bytes: &[u8],
        name: impl Into<String>,
        options: &ShxReadOptions,
    ) -> Result<ShxFont, String> {
        if bytes.len() > options.maximum_bytes {
            return Err("SHX input exceeds configured byte limit.".into());
        }
        let header_end = bytes
            .iter()
            .position(|&b| b == 0x1A)
            .unwrap_or(bytes.len().min(64));
        let header = String::from_utf8_lossy(&bytes[..header_end]);
        let encoding = if header.contains("unifont") {
            ShxEncoding::Unicode
        } else if header.contains("bigfont") {
            ShxEncoding::BigFont
        } else {
            ShxEncoding::Ascii
        };
        Ok(ShxFont {
            name: name.into(),
            encoding,
            embedding: ShxEmbedding::Horizontal,
            above: 6,
            below: 2,
            symbols: BTreeMap::new(),
        })
    }
}

