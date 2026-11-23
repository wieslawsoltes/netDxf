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
pub struct ShxExecutionOptions {
    pub maximum_steps: usize,
    pub maximum_recursion: usize,
    pub maximum_call_depth: usize,
    pub maximum_curves: usize,
    pub maximum_instructions: usize,
    pub maximum_stack_depth: usize,
}

impl Default for ShxExecutionOptions {
    fn default() -> Self {
        Self {
            maximum_steps: 100_000,
            maximum_recursion: 32,
            maximum_call_depth: 32,
            maximum_curves: 100_000,
            maximum_instructions: 100_000,
            maximum_stack_depth: 64,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShxGlyph {
    pub code: u32,
    pub contours: Vec<CadContour>,
    pub advance: f64,
    pub bounds: netdxf_shadow::Bounds2,
    pub executed_instructions: usize,
    pub exit_scale: f64,
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
            bounds: netdxf_shadow::Bounds2::default(),
            executed_instructions: steps,
            exit_scale: scale,
        })
    }
}

