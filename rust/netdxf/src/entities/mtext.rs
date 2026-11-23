use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::mtext_attachment_point::{
    MTextAttachmentPoint, MTextDrawingDirection, MTextLineSpacingStyle,
};
use crate::matrix3::Matrix3;
use crate::tables::TextStyle;
use crate::vector3::Vector3;

/// Represents a DXF `MTEXT` (multiline text) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct MText {
    pub common: EntityCommon,
    pub value: String,
    pub position: Vector3,
    pub height: f64,
    pub rectangle_width: f64,
    pub attachment_point: MTextAttachmentPoint,
    pub drawing_direction: MTextDrawingDirection,
    pub line_spacing_factor: f64,
    pub line_spacing_style: MTextLineSpacingStyle,
    pub rotation: f64,
    pub style: String,
}

impl MText {
    pub fn new(value: impl Into<String>, position: Vector3, height: f64) -> Self {
        Self {
            common: EntityCommon::new(EntityType::MText, DxfObjectCode::MTEXT),
            value: value.into(),
            position,
            height: height.abs().max(f64::EPSILON),
            rectangle_width: 0.0,
            attachment_point: MTextAttachmentPoint::TopLeft,
            drawing_direction: MTextDrawingDirection::ByStyle,
            line_spacing_factor: 1.0,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
            rotation: 0.0,
            style: TextStyle::DEFAULT_NAME.to_string(),
        }
    }

    pub fn plain_text(&self) -> String {
        let mut out = String::with_capacity(self.value.len());
        let mut chars = self.value.chars().peekable();
        while let Some(c) = chars.next() {
            match c {
                '{' | '}' => {}
                '\\' => {
                    if let Some(&next) = chars.peek() {
                        match next {
                            'P' | 'p' => {
                                chars.next();
                                out.push('\n');
                            }
                            '~' => {
                                chars.next();
                                out.push(' ');
                            }
                            '\\' | '{' | '}' => {
                                chars.next();
                                out.push(next);
                            }
                            'L' | 'l' | 'O' | 'o' | 'K' | 'k' => {
                                chars.next();
                            }
                            _ => {
                                for ch in chars.by_ref() {
                                    if ch == ';' {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                _ => out.push(c),
            }
        }
        out
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.position = transformation * self.position + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.common.normal) {
            self.common.normal = n;
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::new(self.position.to_vector2(), self.position.to_vector2())
    }
}
