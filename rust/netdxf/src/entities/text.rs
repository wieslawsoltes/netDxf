use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::text_alignment::TextAlignment;
use crate::matrix3::Matrix3;
use crate::tables::TextStyle;
use crate::vector3::Vector3;

/// Represents a DXF `TEXT` (single-line text) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Text {
    pub common: EntityCommon,
    pub value: String,
    pub position: Vector3,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub rotation: f64,
    pub alignment: TextAlignment,
    pub is_backward: bool,
    pub is_upside_down: bool,
    pub style: String,
    pub thickness: f64,
}

impl Text {
    pub fn new(value: impl Into<String>, position: Vector3, height: f64) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Text, DxfObjectCode::TEXT),
            value: value.into(),
            position,
            height: height.abs().max(f64::EPSILON),
            width_factor: 1.0,
            oblique_angle: 0.0,
            rotation: 0.0,
            alignment: TextAlignment::BaselineLeft,
            is_backward: false,
            is_upside_down: false,
            style: TextStyle::DEFAULT_NAME.to_string(),
            thickness: 0.0,
        }
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
