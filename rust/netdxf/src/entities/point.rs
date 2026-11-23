use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF `POINT` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Point {
    pub common: EntityCommon,
    pub position: Vector3,
    pub thickness: f64,
    pub rotation: f64,
}

impl Point {
    pub fn new(position: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Point, DxfObjectCode::POINT),
            position,
            thickness: 0.0,
            rotation: 0.0,
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
