use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::attribute::Attribute;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF `INSERT` (block reference) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Insert {
    pub common: EntityCommon,
    pub block_name: String,
    pub position: Vector3,
    pub scale: Vector3,
    /// Rotation angle in degrees.
    pub rotation: f64,
    pub attributes: Vec<Attribute>,
    pub column_count: i16,
    pub row_count: i16,
    pub column_spacing: f64,
    pub row_spacing: f64,
}

impl Insert {
    pub fn new(block_name: impl Into<String>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Insert, DxfObjectCode::INSERT),
            block_name: block_name.into(),
            position: Vector3::ZERO,
            scale: Vector3::new(1.0, 1.0, 1.0),
            rotation: 0.0,
            attributes: Vec::new(),
            column_count: 1,
            row_count: 1,
            column_spacing: 0.0,
            row_spacing: 0.0,
        }
    }

    pub fn with_position(block_name: impl Into<String>, position: Vector3) -> Self {
        let mut ins = Self::new(block_name);
        ins.position = position;
        ins
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
