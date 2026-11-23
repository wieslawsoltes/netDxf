use crate::dxf_object::DxfObject;

/// First-class DXF `VBA_PROJECT` opaque binary payload object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfVbaProject {
    pub object: DxfObject,
    pub data: Vec<u8>,
}

impl Default for DxfVbaProject {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfVbaProject {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("VBA_PROJECT"),
            data: Vec::new(),
        }
    }
}
