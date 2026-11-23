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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ShxEncoding {
    #[default]
    Ascii,
    Unicode,
    BigFont,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ShxEmbedding {
    #[default]
    Horizontal,
    DualOrientation,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxInstruction {
    pub opcode: u8,
    pub dx: i8,
    pub dy: i8,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxSymbol {
    pub code: u32,
    pub name: String,
    pub data: Vec<u8>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxReadOptions {
    pub maximum_bytes: usize,
    pub maximum_symbols: usize,
}

impl Default for ShxReadOptions {
    fn default() -> Self {
        Self {
            maximum_bytes: 16 * 1024 * 1024,
            maximum_symbols: 65_536,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxFont {
    pub name: String,
    pub encoding: ShxEncoding,
    pub embedding: ShxEmbedding,
    pub above: u8,
    pub below: u8,
    pub symbols: BTreeMap<u32, ShxSymbol>,
}

impl ShxFont {
    pub fn big_font(&self) -> bool {
        self.encoding == ShxEncoding::BigFont
    }

    pub fn is_font(&self) -> bool {
        self.above > 0
    }

    pub fn cap_height(&self) -> u8 {
        self.above
    }

    pub fn descender(&self) -> u8 {
        self.below
    }

    pub fn supports_vertical_text(&self) -> bool {
        self.embedding == ShxEmbedding::DualOrientation
    }

    pub fn names(&self) -> Vec<&str> {
        self.symbols
            .values()
            .filter(|s| !s.name.is_empty())
            .map(|s| s.name.as_str())
            .collect()
    }
}

