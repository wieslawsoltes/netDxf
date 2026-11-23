use crate::blocks::{Block, BlockTypeFlags};
use crate::entities::{Dimension, DimensionType, Line, MText, MTextAttachmentPoint, Point, Solid};
use crate::math_helper::MathHelper;
use crate::tables::DimensionStyle;
use crate::vector2::Vector2;

/// Synthesizes anonymous `*D<n>` dimension block geometry (`netDxf.Entities.DimensionBlock`).
pub struct DimensionBlock;

impl DimensionBlock {
    /// Builds a complete anonymous dimension block containing defpoints, extension lines,
    /// dimension line, arrowheads, center marks, and formatted dimension text.
    pub fn build(dim: &Dimension, style: &DimensionStyle, block_name: impl Into<String>) -> Block {
        let mut block = Block::new_unchecked(block_name, false);
        block.flags |= BlockTypeFlags::ANONYMOUS_BLOCK;

        let scale = style.dim_scale_overall.max(f64::EPSILON);
        let arrow_len = style.arrow_size * scale;
        let text_height = (style.text_height * scale).max(f64::EPSILON);
        let ext_offset = style.ext_line_offset * scale;
        let ext_extend = style.ext_line_extend * scale;

        match dim.dimension_type {
            DimensionType::Linear | DimensionType::Aligned => {
                let p1 = dim.first_reference_point.to_vector2();
                let p2 = dim.second_reference_point.to_vector2();
                let def = dim.definition_point.to_vector2();

                let dir = if dim.dimension_type == DimensionType::Linear {
                    let rad = dim.rotation * MathHelper::DEG_TO_RAD;
                    Vector2::new(rad.cos(), rad.sin())
                } else {
                    let d = Vector2::normalize(p2 - p1);
                    if Vector2::is_nan(d) { Vector2::UNIT_X } else { d }
                };
                let perp = Vector2::perpendicular(dir);

                // Project p1 and p2 onto the dimension line passing through `def` along `dir`
                let d1 = p1 + perp * Vector2::dot_product(def - p1, perp);
                let d2 = p2 + perp * Vector2::dot_product(def - p2, perp);

                // Defpoints
                Self::add_defpoint(&mut block, p1);
                Self::add_defpoint(&mut block, p2);
                Self::add_defpoint(&mut block, d2);

                // Extension lines
                if !style.ext_line1_off {
                    Self::add_extension_line(&mut block, p1, d1, ext_offset, ext_extend, style);
                }
                if !style.ext_line2_off {
                    Self::add_extension_line(&mut block, p2, d2, ext_offset, ext_extend, style);
                }

                // Dimension line
                if !style.dim_line1_off || !style.dim_line2_off {
                    let mut dim_line = Line::new_2d(d1, d2);
                    dim_line.common.color = style.dim_line_color;
                    dim_line.common.linetype = style.dim_line_linetype.clone();
                    dim_line.common.lineweight = style.dim_line_lineweight;
                    block.add_entity(dim_line);
                }

                // Arrowheads
                let line_dir = Vector2::normalize(d2 - d1);
                if !Vector2::is_nan(line_dir) && arrow_len > 0.0 {
                    Self::add_arrowhead(&mut block, d1, line_dir, arrow_len, style);
                    Self::add_arrowhead(&mut block, d2, -line_dir, arrow_len, style);
                }

                // Dimension text
                let mid = Vector2::mid_point(d1, d2);
                let text_pos = mid + perp * (style.text_offset * scale + text_height * 0.5);
                let text_str = Self::format_measurement(dim, style);
                let mut mtext = MText::new(text_str, text_pos.to_vector3(dim.elevation), text_height);
                mtext.attachment_point = MTextAttachmentPoint::MiddleCenter;
                mtext.style = style.text_style.clone();
                mtext.common.color = style.text_color;
                mtext.rotation = Vector2::angle(dir) * MathHelper::RAD_TO_DEG;
                block.add_entity(mtext);
            }
            DimensionType::Radius | DimensionType::Diameter => {
                let center = dim.definition_point.to_vector2();
                let edge = dim.first_reference_point.to_vector2();
                Self::add_defpoint(&mut block, center);
                Self::add_defpoint(&mut block, edge);

                let dir = Vector2::normalize(edge - center);
                let start = if dim.dimension_type == DimensionType::Diameter && !Vector2::is_nan(dir) {
                    center - dir * Vector2::distance(center, edge)
                } else {
                    center
                };

                let mut dim_line = Line::new_2d(start, edge);
                dim_line.common.color = style.dim_line_color;
                dim_line.common.linetype = style.dim_line_linetype.clone();
                dim_line.common.lineweight = style.dim_line_lineweight;
                block.add_entity(dim_line);

                if !Vector2::is_nan(dir) && arrow_len > 0.0 {
                    Self::add_arrowhead(&mut block, edge, -dir, arrow_len, style);
                    if dim.dimension_type == DimensionType::Diameter {
                        Self::add_arrowhead(&mut block, start, dir, arrow_len, style);
                    }
                }

                // Center mark cross
                let cm = style.center_mark_size.abs() * scale;
                if cm > 0.0 {
                    let mut h = Line::new_2d(
                        Vector2::new(center.x - cm, center.y),
                        Vector2::new(center.x + cm, center.y),
                    );
                    h.common.color = style.dim_line_color;
                    let mut v = Line::new_2d(
                        Vector2::new(center.x, center.y - cm),
                        Vector2::new(center.x, center.y + cm),
                    );
                    v.common.color = style.dim_line_color;
                    block.add_entity(h);
                    block.add_entity(v);
                }

                let text_pos = Vector2::mid_point(start, edge);
                let prefix = if dim.dimension_type == DimensionType::Diameter { "%%c" } else { "R" };
                let text_str = match &dim.user_text {
                    Some(ut) if !ut.is_empty() => ut.clone(),
                    _ => format!("{prefix}{}", Self::format_measurement(dim, style)),
                };
                let mut mtext = MText::new(text_str, text_pos.to_vector3(dim.elevation), text_height);
                mtext.attachment_point = MTextAttachmentPoint::MiddleCenter;
                mtext.style = style.text_style.clone();
                mtext.common.color = style.text_color;
                block.add_entity(mtext);
            }
            _ => {
                // Generic fallback: defpoint + measurement text
                let pos = dim.text_reference_point;
                Self::add_defpoint(&mut block, dim.definition_point.to_vector2());
                let text_str = Self::format_measurement(dim, style);
                let mut mtext = MText::new(text_str, pos.to_vector3(dim.elevation), text_height);
                mtext.attachment_point = MTextAttachmentPoint::MiddleCenter;
                mtext.style = style.text_style.clone();
                mtext.common.color = style.text_color;
                block.add_entity(mtext);
            }
        }

        block
    }

    fn add_defpoint(block: &mut Block, pt: Vector2) {
        let mut p = Point::new(pt.to_vector3(0.0));
        p.common.layer = "Defpoints".to_string();
        block.add_entity(p);
    }

    fn add_extension_line(
        block: &mut Block,
        origin: Vector2,
        target: Vector2,
        offset: f64,
        extend: f64,
        style: &DimensionStyle,
    ) {
        let d = Vector2::normalize(target - origin);
        if Vector2::is_nan(d) {
            return;
        }
        let start = origin + d * offset;
        let end = target + d * extend;
        let mut line = Line::new_2d(start, end);
        line.common.color = style.ext_line_color;
        line.common.linetype = style.ext_line1_linetype.clone();
        line.common.lineweight = style.ext_line_lineweight;
        block.add_entity(line);
    }

    fn add_arrowhead(
        block: &mut Block,
        tip: Vector2,
        dir_into_line: Vector2,
        length: f64,
        style: &DimensionStyle,
    ) {
        let perp = Vector2::perpendicular(dir_into_line);
        let half_w = length / 6.0;
        let base = tip + dir_into_line * length;
        let b1 = base + perp * half_w;
        let b2 = base - perp * half_w;
        let mut arrow = Solid::new(tip, b1, b2, b2);
        arrow.common.color = style.dim_line_color;
        block.add_entity(arrow);
    }

    pub fn format_measurement(dim: &Dimension, style: &DimensionStyle) -> String {
        if let Some(ut) = &dim.user_text {
            if !ut.is_empty() && ut != "<>" {
                return ut.clone();
            }
        }
        let mut value = dim.measurement * style.dim_scale_linear;
        if style.dim_roundoff > 0.0 {
            value = MathHelper::round_to_nearest(value, style.dim_roundoff);
        }
        let prec = style.length_precision.clamp(0, 8) as usize;
        let mut formatted = format!("{value:.prec$}");
        if style.suppress_linear_trailing_zeros && prec > 0 && formatted.contains('.') {
            while formatted.ends_with('0') {
                formatted.pop();
            }
            if formatted.ends_with('.') {
                formatted.pop();
            }
        }
        if style.decimal_separator != '.' {
            formatted = formatted.replace('.', &style.decimal_separator.to_string());
        }
        format!("{}{formatted}{}", style.dim_prefix, style.dim_suffix)
    }
}
