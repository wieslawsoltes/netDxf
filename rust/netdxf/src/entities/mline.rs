use bitflags::bitflags;

use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::mline_justification::MLineJustification;
use crate::vector2::Vector2;

bitflags! {
    /// MLine flags (group code 71).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct MLineFlags: i16 {
        const HAS = 1;
        const CLOSED = 2;
        const NO_START_CAPS = 4;
        const NO_END_CAPS = 8;
    }
}

/// Represents a vertex of an `MLine`.
#[derive(Clone, Debug, PartialEq)]
pub struct MLineVertex {
    pub position: Vector2,
    pub direction: Vector2,
    pub miter: Vector2,
    pub distances: Vec<Vec<f64>>,
}

impl MLineVertex {
    pub fn new(position: Vector2) -> Self {
        Self {
            position,
            direction: Vector2::UNIT_X,
            miter: Vector2::UNIT_Y,
            distances: Vec::new(),
        }
    }
}

/// Represents a DXF `MLINE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct MLine {
    pub common: EntityCommon,
    pub style: String,
    pub scale: f64,
    pub justification: MLineJustification,
    pub flags: MLineFlags,
    pub elevation: f64,
    pub vertexes: Vec<MLineVertex>,
}

impl MLine {
    pub fn new(vertexes: Vec<Vector2>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::MLine, DxfObjectCode::MLINE),
            style: "Standard".to_string(),
            scale: 1.0,
            justification: MLineJustification::Top,
            flags: MLineFlags::HAS,
            elevation: 0.0,
            vertexes: vertexes.into_iter().map(MLineVertex::new).collect(),
        }
    }
}
