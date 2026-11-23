use crate::dxf_object::DxfObject;

/// Represents a DXF `LAYER_INDEX` object (`netDxf.Objects.DxfLayerIndex`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfLayerIndex {
    pub object: DxfObject,
    pub timestamp: f64,
    pub entries: Vec<(String, i32)>,
}

impl Default for DxfLayerIndex {
    fn default() -> Self {
        Self {
            object: DxfObject::new("LAYER_INDEX"),
            timestamp: 0.0,
            entries: Vec::new(),
        }
    }
}

/// Represents a DXF `LAYER_FILTER` object (`netDxf.Objects.DxfLayerFilter`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfLayerFilter {
    pub object: DxfObject,
    pub layer_names: Vec<String>,
}

impl Default for DxfLayerFilter {
    fn default() -> Self {
        Self {
            object: DxfObject::new("LAYER_FILTER"),
            layer_names: Vec::new(),
        }
    }
}

/// Single layer entry inside a [`DxfLayerIndex`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfLayerIndexEntry {
    pub layer_name: String,
    pub id_buffer_handle: String,
    pub entity_count: i32,
}
