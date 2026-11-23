use crate::entities::{MTextAttachmentPoint, MTextBackgroundFill, MTextColumns, MTextDrawingDirection, MTextLineSpacingStyle};
use crate::vector3::Vector3;

/// Stored multiline attribute (`MTEXT`) payload attached to an `Attribute` or `AttributeDefinition`.
#[derive(Debug, Clone, PartialEq)]
pub struct AttributeMText {
    pub value: String,
    pub position: Vector3,
    pub height: f64,
    pub rectangle_width: f64,
    pub attachment_point: MTextAttachmentPoint,
    pub drawing_direction: MTextDrawingDirection,
    pub line_spacing_factor: f64,
    pub line_spacing_style: MTextLineSpacingStyle,
    pub rotation: f64,
    pub normal: Vector3,
    pub style: String,
    pub background_fill: Option<MTextBackgroundFill>,
    pub columns: Option<MTextColumns>,
}

impl Default for AttributeMText {
    fn default() -> Self {
        Self {
            value: String::new(),
            position: Vector3::ZERO,
            height: 1.0,
            rectangle_width: 0.0,
            attachment_point: MTextAttachmentPoint::TopLeft,
            drawing_direction: MTextDrawingDirection::ByStyle,
            line_spacing_factor: 1.0,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
            rotation: 0.0,
            normal: Vector3::UNIT_Z,
            style: "Standard".to_string(),
            background_fill: None,
            columns: None,
        }
    }
}
