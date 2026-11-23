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

impl AttributeMText {
    #[inline] pub fn defined_height(&self) -> f64 { self.height }
    #[inline] pub fn actual_height(&self) -> f64 { self.height }
    #[inline] pub fn actual_width(&self) -> f64 { self.rectangle_width }
    #[inline] pub fn text_direction(&self) -> Vector3 { Vector3::UNIT_X }

    pub fn to_mtext(&self) -> crate::entities::MText {
        let mut mt = crate::entities::MText::new(self.value.clone(), self.position, self.height);
        mt.rectangle_width = self.rectangle_width;
        mt.attachment_point = self.attachment_point;
        mt.drawing_direction = self.drawing_direction;
        mt.line_spacing_factor = self.line_spacing_factor;
        mt.line_spacing_style = self.line_spacing_style;
        mt.rotation = self.rotation;
        mt.common.normal = self.normal;
        mt.style = self.style.clone();
        mt.background_fill = self.background_fill.clone();
        mt.columns = self.columns.clone();
        mt
    }

    pub fn from_stored_fields(value: impl Into<String>, position: Vector3, height: f64) -> Self {
        Self {
            value: value.into(),
            position,
            height,
            ..Default::default()
        }
    }

    pub fn get_stored_fields(&self) -> (&str, Vector3, f64) {
        (&self.value, self.position, self.height)
    }
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
