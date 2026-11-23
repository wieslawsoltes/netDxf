use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

/// Represents a DXF `HELIX` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Helix {
    pub common: EntityCommon,
    pub axis_base_point: Vector3,
    pub start_point: Vector3,
    pub axis_vector: Vector3,
    pub radius: f64,
    pub turns: f64,
    pub turn_height: f64,
    pub handedness: bool,
    pub constrain: i16,
}

impl Helix {
    pub fn new(axis_base_point: Vector3, start_point: Vector3, turns: f64, turn_height: f64) -> Self {
        let radius = Vector3::distance(axis_base_point, start_point);
        Self {
            common: EntityCommon::new(EntityType::Helix, DxfObjectCode::HELIX),
            axis_base_point,
            start_point,
            axis_vector: Vector3::UNIT_Z,
            radius,
            turns,
            turn_height,
            handedness: true,
            constrain: 0,
        }
    }
}

/// Helix geometric constraint mode (DXF group 280).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum HelixConstraint {
    TurnHeight = 0,
    #[default]
    Turns = 1,
    Height = 2,
}
