use crate::dxf_object::DxfObject;

/// First-class DXF `WIPEOUTVARIABLES` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfWipeoutVariables {
    pub object: DxfObject,
    pub display_frame: i16,
}

impl Default for DxfWipeoutVariables {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfWipeoutVariables {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("WIPEOUTVARIABLES"),
            display_frame: 1,
        }
    }
}
