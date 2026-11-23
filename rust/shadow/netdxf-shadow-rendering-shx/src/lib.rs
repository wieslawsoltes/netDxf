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
    /// Interprets AutoCAD SHX vector bytecode (pen up/down, octant vectors, XY displacements, push/pop, scale).
    pub fn compile(font: &ShxFont, code: u32, options: &ShxExecutionOptions) -> Option<ShxGlyph> {
        let sym = font.symbols.get(&code)?;
        let mut x = 0.0f64;
        let mut y = 0.0f64;
        let mut pen_down = true;
        let mut scale = 1.0f64;
        let mut stack: Vec<(f64, f64)> = Vec::new();
        let mut curves = Vec::new();
        let mut i = 0usize;
        let mut steps = 0usize;

        while i < sym.data.len() && steps < options.maximum_steps {
            steps += 1;
            let op = sym.data[i];
            i += 1;
            match op {
                0 => break, // End of shape
                1 => pen_down = true,
                2 => pen_down = false,
                3 if i < sym.data.len() => {
                    let d = sym.data[i] as f64;
                    if d > 0.0 {
                        scale /= d;
                    }
                    i += 1;
                }
                4 if i < sym.data.len() => {
                    scale *= sym.data[i] as f64;
                    i += 1;
                }
                5 => stack.push((x, y)),
                6 => {
                    if let Some((px, py)) = stack.pop() {
                        x = px;
                        y = py;
                    }
                }
                8 if i + 1 < sym.data.len() => {
                    let dx = (sym.data[i] as i8) as f64 * scale;
                    let dy = (sym.data[i + 1] as i8) as f64 * scale;
                    i += 2;
                    let nx = x + dx;
                    let ny = y + dy;
                    if pen_down {
                        curves.push(netdxf_shadow_geometry::Curve3::Line(
                            netdxf_shadow_geometry::LineCurve {
                                a: netdxf_shadow::Vec3::xy(x, y),
                                b: netdxf_shadow::Vec3::xy(nx, ny),
                            },
                        ));
                    }
                    x = nx;
                    y = ny;
                }
                v if v >= 16 => {
                    let len = ((v >> 4) as f64) * scale;
                    let dir = v & 0x0F;
                    let (ux, uy) = match dir {
                        0 => (1.0, 0.0),
                        1 => (1.0, 0.5),
                        2 => (1.0, 1.0),
                        3 => (0.5, 1.0),
                        4 => (0.0, 1.0),
                        5 => (-0.5, 1.0),
                        6 => (-1.0, 1.0),
                        7 => (-1.0, 0.5),
                        8 => (-1.0, 0.0),
                        9 => (-1.0, -0.5),
                        10 => (-1.0, -1.0),
                        11 => (-0.5, -1.0),
                        12 => (0.0, -1.0),
                        13 => (0.5, -1.0),
                        14 => (1.0, -1.0),
                        _ => (1.0, -0.5),
                    };
                    let nx = x + ux * len;
                    let ny = y + uy * len;
                    if pen_down {
                        curves.push(netdxf_shadow_geometry::Curve3::Line(
                            netdxf_shadow_geometry::LineCurve {
                                a: netdxf_shadow::Vec3::xy(x, y),
                                b: netdxf_shadow::Vec3::xy(nx, ny),
                            },
                        ));
                    }
                    x = nx;
                    y = ny;
                }
                _ => {}
            }
        }

        let contours = if curves.is_empty() {
            Vec::new()
        } else {
            vec![CadContour {
                curves,
                closed: false,
            }]
        };
        Some(ShxGlyph {
            code,
            contours,
            advance: x.max(font.above as f64),
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
