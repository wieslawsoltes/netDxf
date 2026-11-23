use indexmap::IndexMap;

use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;

/// Represents a DXF `DICTIONARY` object.
#[derive(Clone, Debug, PartialEq)]
pub struct DictionaryObject {
    pub object: DxfObject,
    pub is_hard_owner: bool,
    pub cloning_flags: i16,
    pub entries: IndexMap<String, String>,
}

impl DictionaryObject {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::DICTIONARY),
            is_hard_owner: false,
            cloning_flags: 1,
            entries: IndexMap::new(),
        }
    }
}

impl Default for DictionaryObject {
    fn default() -> Self {
        Self::new()
    }
}
