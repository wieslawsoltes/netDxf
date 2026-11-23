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
    pub metadata_fields: Ole2FrameMetadataFields,
    pub additional_metadata: Vec<(i32, String)>,
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
            metadata_fields: Ole2FrameMetadataFields::ALL,
            additional_metadata: Vec::new(),
        }
    }

    #[inline] pub fn description(&self) -> &str { &self.ole_client_type }
    #[inline] pub fn binary_data_length(&self) -> usize { self.binary_data.len() }
    #[inline] pub fn get_binary_data(&self) -> &[u8] { &self.binary_data }
    #[inline] pub fn object_type(&self) -> OleObjectType {
        match self.ole_object_type {
            1 => OleObjectType::Link,
            3 => OleObjectType::Static,
            _ => OleObjectType::Embedded,
        }
    }
    pub fn with_metadata_fields(mut self, fields: Ole2FrameMetadataFields) -> Self {
        self.metadata_fields = fields;
        self
    }
    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        self.upper_left_corner = transformation * self.upper_left_corner + translation;
        self.lower_right_corner = transformation * self.lower_right_corner + translation;
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

/// OLE object relationship type (DXF group 71).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum OleObjectType {
    Link = 1,
    #[default]
    Embedded = 2,
    Static = 3,
}

bitflags::bitflags! {
    /// Presence bitmask for optional informational metadata fields on `Ole2Frame`.
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct Ole2FrameMetadataFields: i32 {
        const NONE = 0;
        const OLE_VERSION = 1;
        const DESCRIPTION = 2;
        const UPPER_LEFT_CORNER = 4;
        const LOWER_RIGHT_CORNER = 8;
        const OBJECT_TYPE = 16;
        const TILE_MODE = 32;
        const ALL = 1 | 2 | 4 | 8 | 16 | 32;
    }
}
