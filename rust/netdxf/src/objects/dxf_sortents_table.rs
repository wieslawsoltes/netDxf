use crate::dxf_object::DxfObject;

/// Single entity-to-sort-handle entry in a [`DxfSortentsTable`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfSortOrderEntry {
    pub entity_handle: String,
    pub sort_handle: String,
}

impl DxfSortOrderEntry {
    #[inline] pub fn entity(&self) -> &str { &self.entity_handle }
    #[inline] pub fn target(&self) -> &str { &self.sort_handle }
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

    #[inline]
    pub fn block_record(&self) -> Option<&str> {
        self.block_record_handle.as_deref()
    }
}
