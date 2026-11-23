use crate::{ShxExecutionOptions, ShxFont, ShxFontCatalog, ShxGlyphCompiler};
use netdxf_shadow::{Affine3, ObjectId, ShadowSnapshot, Vec3};
use netdxf_shadow_geometry::{
    CadContour, CadGeometry, Curve3, ICacheableEntityProjector, IEntityProjector, LineCurve,
};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxProjectionOptions {
    pub fallback_to_ttf: bool,
    pub maximum_cached_glyphs_per_font: usize,
    pub maximum_characters: usize,
    pub maximum_curves: usize,
    pub maximum_glyph_instructions: usize,
}

impl Default for ShxProjectionOptions {
    fn default() -> Self {
        Self {
            fallback_to_ttf: true,
            maximum_cached_glyphs_per_font: 2_048,
            maximum_characters: 16_384,
            maximum_curves: 100_000,
            maximum_glyph_instructions: 100_000,
        }
    }
}

/// Projects `Text`, `MText`, `Attribute`, `AttributeDefinition`, and `Shape` entities whose style
/// references a loaded SHX font into stroked [`CadGeometry`] contours (`ShxEntityProjector.cs`).
#[derive(Debug, Clone, Default)]
pub struct ShxEntityProjector {
    pub catalog: ShxFontCatalog,
    pub options: ShxProjectionOptions,
}

impl ShxEntityProjector {
    pub fn try_project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        self.project(snapshot, entity_id)
    }

    fn resolve_font<'a>(&'a self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<&'a ShxFont> {
        let reader = snapshot.read(entity_id.clone());
        let style = reader.field("Style");
        let font_file = if style.exists() {
            let f = style.field("FontFile").text().to_string();
            if !f.is_empty() {
                f
            } else {
                style.field("File").text().to_string()
            }
        } else {
            String::new()
        };
        if !font_file.is_empty() {
            if let Some(font) = self.catalog.set.resolve(&font_file) {
                return Some(font);
            }
            let base = font_file.rsplit(['/', '\\']).next().unwrap_or(&font_file);
            if let Some(font) = self.catalog.set.resolve(base) {
                return Some(font);
            }
        }
        None
    }
}

impl IEntityProjector for ShxEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        let ty = reader.type_name();
        if !matches!(
            ty,
            "Text" | "MText" | "Shape" | "Attribute" | "AttributeDefinition"
        ) {
            return None;
        }

        let Some(font) = self.resolve_font(snapshot, entity_id) else {
            // If no SHX font is loaded for this entity and fallback is allowed, let the next
            // projector handle it (or return empty if no fallback).
            return if self.options.fallback_to_ttf {
                None
            } else {
                Some(CadGeometry::default())
            };
        };

        let position = reader.field("Position").vector(Vec3::ZERO);
        let normal = reader.field("Normal").vector(Vec3::UNIT_Z);
        let basis = Affine3::object_coordinates(if normal.length() > 0.0 {
            normal
        } else {
            Vec3::UNIT_Z
        });
        let rotation = reader.field("Rotation").number(0.0).to_radians();
        let height = reader
            .field("Height")
            .number(reader.field("Size").number(1.0))
            .max(1e-6);
        let width_factor = reader.field("WidthFactor").number(1.0).max(1e-6);
        let oblique = reader.field("ObliqueAngle").number(0.0).to_radians().tan();
        let units_above = (font.above as f64).max(1.0);
        let scale = height / units_above;
        let exec_opts = ShxExecutionOptions {
            maximum_instructions: self.options.maximum_glyph_instructions.max(1_000),
            maximum_curves: self.options.maximum_curves.max(1_000),
            ..Default::default()
        };

        let frame = Affine3::move_by(position) * basis * Affine3::rotate_z(rotation);
        let mut out_contours = Vec::new();
        let mut total_curves = 0usize;

        let map_pt = |gx: f64, gy: f64, cursor_x: f64, cursor_y: f64| -> Vec3 {
            let lx = (cursor_x + gx * width_factor + gy * oblique) * scale;
            let ly = (cursor_y + gy) * scale;
            frame.point(Vec3::new(lx, ly, 0.0))
        };

        if ty == "Shape" {
            let shape_name = reader.field("Name").text().to_string();
            let shape_code = font
                .symbols
                .values()
                .find(|s| s.name.eq_ignore_ascii_case(&shape_name))
                .map(|s| s.code);
            if let Some(code) = shape_code {
                if let Some(glyph) = ShxGlyphCompiler::compile(font, code, &exec_opts) {
                    for c in glyph.contours {
                        let curves: Vec<Curve3> = c
                            .curves
                            .into_iter()
                            .filter_map(|cv| match cv {
                                Curve3::Line(l) => Some(Curve3::Line(LineCurve {
                                    a: map_pt(l.a.x, l.a.y, 0.0, 0.0),
                                    b: map_pt(l.b.x, l.b.y, 0.0, 0.0),
                                })),
                                _ => None,
                            })
                            .collect();
                        if !curves.is_empty() {
                            out_contours.push(CadContour {
                                curves,
                                closed: c.closed,
                            });
                        }
                    }
                }
            }
        } else {
            let text = reader.field("Value").text().to_string();
            let mut cursor_x = 0.0f64;
            let mut cursor_y = 0.0f64;
            let line_step = (font.above as f64 + font.below as f64).max(units_above) * 1.35;

            for ch in text.chars().take(self.options.maximum_characters.max(1)) {
                if ch == '\n' {
                    cursor_x = 0.0;
                    cursor_y -= line_step;
                    continue;
                }
                if ch == '\r' {
                    continue;
                }
                let code = ch as u32;
                if let Some(glyph) = ShxGlyphCompiler::compile(font, code, &exec_opts) {
                    for c in glyph.contours {
                        let mut curves = Vec::with_capacity(c.curves.len());
                        for cv in c.curves {
                            if total_curves >= self.options.maximum_curves.max(1_000) {
                                break;
                            }
                            if let Curve3::Line(l) = cv {
                                curves.push(Curve3::Line(LineCurve {
                                    a: map_pt(l.a.x, l.a.y, cursor_x, cursor_y),
                                    b: map_pt(l.b.x, l.b.y, cursor_x, cursor_y),
                                }));
                                total_curves += 1;
                            }
                        }
                        if !curves.is_empty() {
                            out_contours.push(CadContour {
                                curves,
                                closed: c.closed,
                            });
                        }
                    }
                    let adv = if glyph.advance.abs() > 1e-6 {
                        glyph.advance
                    } else if !glyph.bounds.is_empty() {
                        glyph.bounds.width()
                    } else {
                        units_above * 0.6
                    };
                    cursor_x += adv * width_factor;
                } else {
                    cursor_x += units_above * 0.6 * width_factor;
                }
            }
        }

        Some(CadGeometry {
            contours: out_contours,
            ..Default::default()
        })
    }
}

impl ICacheableEntityProjector for ShxEntityProjector {
    fn projector_key(&self) -> &str {
        "shx"
    }

    fn revision(&self) -> i64 {
        self.catalog.set.revision
    }
}
