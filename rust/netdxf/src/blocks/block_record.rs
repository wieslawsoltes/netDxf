use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::units::DrawingUnits;

/// Represents a DXF `BLOCK_RECORD` table entry.
#[derive(Clone, Debug, PartialEq)]
pub struct BlockRecord {
    pub object: DxfObject,
    pub name: String,
    pub units: DrawingUnits,
    pub allow_exploding: bool,
    pub scale_uniformly: bool,
    pub layout_handle: Option<String>,
}

impl BlockRecord {
    pub fn new(name: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::BLOCK_RECORD),
            name: name.into(),
            units: DrawingUnits::Unitless,
            allow_exploding: true,
            scale_uniformly: false,
            layout_handle: None,
        }
    }
}
