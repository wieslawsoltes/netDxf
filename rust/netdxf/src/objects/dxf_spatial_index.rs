use crate::dxf_object::DxfObject;

/// First-class DXF `SPATIAL_INDEX` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfSpatialIndex {
    pub object: DxfObject,
    pub timestamp: f64,
}

impl Default for DxfSpatialIndex {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfSpatialIndex {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("SPATIAL_INDEX"),
            timestamp: 0.0,
        }
    }
}
