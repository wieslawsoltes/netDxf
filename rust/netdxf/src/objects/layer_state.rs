use indexmap::IndexMap;

use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::lineweight::Lineweight;
use crate::tables::{Layer, TableObject, TableObjectData};
use crate::transparency::Transparency;

/// Snapshot of a layer's properties inside a `LayerState`.
#[derive(Clone, Debug, PartialEq)]
pub struct LayerStateProperties {
    pub name: String,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_locked: bool,
    pub plot: bool,
    pub color: AciColor,
    pub linetype: String,
    pub lineweight: Lineweight,
    pub transparency: Transparency,
}

impl From<&Layer> for LayerStateProperties {
    fn from(layer: &Layer) -> Self {
        Self {
            name: layer.name().to_string(),
            is_visible: layer.is_visible,
            is_frozen: layer.is_frozen,
            is_locked: layer.is_locked,
            plot: layer.plot,
            color: layer.color,
            linetype: layer.linetype.clone(),
            lineweight: layer.lineweight,
            transparency: layer.transparency,
        }
    }
}

/// Represents a saved DXF `LayerState` (`ACAD_LAYERSTATES`).
#[derive(Clone, Debug, PartialEq)]
pub struct LayerState {
    pub table: TableObjectData,
    pub description: String,
    pub current_layer: String,
    pub paper_space: bool,
    pub properties: IndexMap<String, LayerStateProperties>,
}

impl LayerState {
    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::XRECORD, true)?,
            description: String::new(),
            current_layer: Layer::DEFAULT_NAME.to_string(),
            paper_space: false,
            properties: IndexMap::new(),
        })
    }
}

impl TableObject for LayerState {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
