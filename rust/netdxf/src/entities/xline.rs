use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF `XLINE` (infinite construction line) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct XLine {
    pub common: EntityCommon,
    pub origin: Vector3,
    pub direction: Vector3,
}

impl XLine {
    pub fn new(origin: Vector3, direction: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::XLine, DxfObjectCode::XLINE),
            origin,
            direction: Vector3::normalize_finite_direction(direction).unwrap_or(Vector3::UNIT_X),
        }
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.origin = transformation * self.origin + translation;
        if let Some(d) = Vector3::normalize_finite_direction(transformation * self.direction) {
            self.direction = d;
        }
    }
}
