use crate::dxf_object::DxfObject;

/// Single entity-to-sort-handle entry in a [`DxfSortentsTable`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfSortOrderEntry {
    pub entity_handle: String,
    pub sort_handle: String,
}

/// First-class DXF `SORTENTSTABLE` draw-order object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfSortentsTable {
    pub object: DxfObject,
    pub block_record_handle: Option<String>,
    pub entries: Vec<DxfSortOrderEntry>,
}

impl Default for DxfSortentsTable {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfSortentsTable {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new("SORTENTSTABLE"),
            block_record_handle: None,
            entries: Vec::new(),
        }
    }
}
