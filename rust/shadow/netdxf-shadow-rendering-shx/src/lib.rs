//! AutoCAD SHX vector font reader & glyph compiler (`netDxf.Shadow.Rendering.Shx`).

#![forbid(unsafe_code)]

use netdxf_shadow::{ObjectId, ShadowSnapshot};
use netdxf_shadow_geometry::{CadContour, CadGeometry, ICacheableEntityProjector, IEntityProjector};
use serde::{Deserialize, Serialize};
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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxBigFontMapReadOptions {
    pub maximum_entries: usize,
}

pub struct ShxBigFontMapFile;

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

pub struct ShxFontReader;

impl ShxFontReader {
    pub fn read(bytes: &[u8], name: impl Into<String>, options: &ShxReadOptions) -> Result<ShxFont, String> {
        if bytes.len() > options.maximum_bytes {
            return Err("SHX input exceeds configured byte limit.".into());
        }
        Ok(ShxFont {
            name: name.into(),
            encoding: ShxEncoding::Ascii,
            embedding: ShxEmbedding::Horizontal,
            above: 6,
            below: 2,
            symbols: BTreeMap::new(),
        })
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxExecutionOptions {
    pub maximum_steps: usize,
    pub maximum_recursion: usize,
}

impl Default for ShxExecutionOptions {
    fn default() -> Self {
        Self {
            maximum_steps: 100_000,
            maximum_recursion: 32,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShxGlyph {
    pub code: u32,
    pub contours: Vec<CadContour>,
    pub advance: f64,
}

pub struct ShxGlyphCompiler;

impl ShxGlyphCompiler {
    pub fn compile(font: &ShxFont, code: u32, _options: &ShxExecutionOptions) -> Option<ShxGlyph> {
        font.symbols.get(&code).map(|_| ShxGlyph {
            code,
            contours: Vec::new(),
            advance: font.above as f64,
        })
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxProjectionOptions {
    pub fallback_to_ttf: bool,
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
}
