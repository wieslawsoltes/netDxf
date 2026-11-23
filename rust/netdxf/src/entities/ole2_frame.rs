use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

/// Represents a DXF `OLE2FRAME` entity (`netDxf.Entities.Ole2Frame`).
#[derive(Clone, Debug, PartialEq)]
pub struct Ole2Frame {
    pub common: EntityCommon,
    pub ole_version: i16,
    pub ole_client_type: String,
    pub upper_left_corner: Vector3,
    pub lower_right_corner: Vector3,
    pub ole_object_type: i16,
    pub tile_mode: i16,
    pub binary_data: Vec<u8>,
}

impl Ole2Frame {
    pub fn new(upper_left: Vector3, lower_right: Vector3, binary_data: Vec<u8>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Ole2Frame, DxfObjectCode::OLE2_FRAME),
            ole_version: 2,
            ole_client_type: String::new(),
            upper_left_corner: upper_left,
            lower_right_corner: lower_right,
            ole_object_type: 2,
            tile_mode: 0,
            binary_data,
        }
    }
}

/// Represents a legacy DXF `OLEFRAME` entity (`netDxf.Entities.OleFrame`).
#[derive(Clone, Debug, PartialEq)]
pub struct OleFrame {
    pub common: EntityCommon,
    pub ole_version: i16,
    pub binary_data: Vec<u8>,
}

impl OleFrame {
    pub fn new(binary_data: Vec<u8>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::OleFrame, DxfObjectCode::OLE_FRAME),
            ole_version: 2,
            binary_data,
        }
    }
}
