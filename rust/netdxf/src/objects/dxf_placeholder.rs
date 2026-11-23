use crate::dxf_object::DxfObject;

/// First-class DXF `ACDBPLACEHOLDER` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfPlaceholder {
    pub object: DxfObject,
}

impl Default for DxfPlaceholder {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfPlaceholder {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("ACDBPLACEHOLDER"),
        }
    }
}
