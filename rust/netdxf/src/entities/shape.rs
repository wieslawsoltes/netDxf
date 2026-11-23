use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

/// Represents a DXF `SHAPE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Shape {
    pub common: EntityCommon,
    pub name: String,
    pub style: String,
    pub position: Vector3,
    pub size: f64,
    pub rotation: f64,
    pub oblique_angle: f64,
    pub width_factor: f64,
    pub thickness: f64,
}

impl Shape {
    pub fn new(
        name: impl Into<String>,
        style: impl Into<String>,
        position: Vector3,
        size: f64,
    ) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Shape, DxfObjectCode::SHAPE),
            name: name.into(),
            style: style.into(),
            position,
            size,
            rotation: 0.0,
            oblique_angle: 0.0,
            width_factor: 1.0,
            thickness: 0.0,
        }
    }
}
