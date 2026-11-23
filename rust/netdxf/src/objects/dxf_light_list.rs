use crate::dxf_object::DxfObject;

/// Single light reference entry in a [`DxfLightList`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfLightListEntry {
    pub light_handle: String,
}

impl DxfLightListEntry {
    #[inline] pub fn light(&self) -> &str { &self.light_handle }
    #[inline] pub fn name(&self) -> &str { &self.light_handle }
}

/// First-class DXF `LIGHTLIST` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfLightList {
    pub object: DxfObject,
    pub version: i32,
    pub entries: Vec<DxfLightListEntry>,
}

impl Default for DxfLightList {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfLightList {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("LIGHTLIST"),
            version: 1,
            entries: Vec::new(),
        }
    }
}
