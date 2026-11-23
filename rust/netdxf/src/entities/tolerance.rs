use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::tables::DimensionStyle;
use crate::vector3::Vector3;

/// Represents a DXF `TOLERANCE` (geometric tolerance) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Tolerance {
    pub common: EntityCommon,
    pub style: String,
    pub position: Vector3,
    pub rotation: f64,
    pub raw_string: String,
}

impl Tolerance {
    pub fn new(raw_string: impl Into<String>, position: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Tolerance, DxfObjectCode::TOLERANCE),
            style: DimensionStyle::DEFAULT_NAME.to_string(),
            position,
            rotation: 0.0,
            raw_string: raw_string.into(),
        }
    }
}
