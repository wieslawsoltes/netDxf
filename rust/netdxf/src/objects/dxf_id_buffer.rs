use crate::dxf_object::DxfObject;

/// First-class DXF `IDBUFFER` database object holding soft-pointer object handles.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfIdBuffer {
    pub object: DxfObject,
    pub object_handles: Vec<String>,
}

impl Default for DxfIdBuffer {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfIdBuffer {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("IDBUFFER"),
            object_handles: Vec::new(),
        }
    }

    #[inline]
    pub fn references(&self) -> &[String] {
        &self.object_handles
    }
}
