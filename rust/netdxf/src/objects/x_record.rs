use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::DxfTag;

/// Represents a DXF `XRECORD` object.
#[derive(Clone, Debug, PartialEq)]
pub struct XRecord {
    pub object: DxfObject,
    pub cloning_flags: i16,
    pub entries: Vec<DxfTag>,
}

impl XRecord {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::XRECORD),
            cloning_flags: 1,
            entries: Vec::new(),
        }
    }
}

impl Default for XRecord {
    fn default() -> Self {
        Self::new()
    }
}
