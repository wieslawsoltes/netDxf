use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::tables::Layer;

/// Represents the `ENDBLK` termination object of a DXF block.
#[derive(Clone, Debug, PartialEq)]
pub struct EndBlock {
    pub object: DxfObject,
    pub layer: String,
}

impl EndBlock {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::BLOCK_END),
            layer: Layer::DEFAULT_NAME.to_string(),
        }
    }
}

impl Default for EndBlock {
    fn default() -> Self {
        Self::new()
    }
}
