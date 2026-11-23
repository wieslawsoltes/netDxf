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

    pub const DEFAULT_UNITS: DrawingUnits = DrawingUnits::Unitless;
    #[inline] pub fn is_for_internal_use_only(&self) -> bool { self.name.starts_with('*') }
    #[inline] pub fn layout(&self) -> Option<&str> { self.layout_handle.as_deref() }
    #[inline] pub fn owner(&self) -> Option<&str> { self.object.owner_handle.as_deref() }
}
