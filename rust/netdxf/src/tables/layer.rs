use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::lineweight::Lineweight;
use crate::tables::layer_flags::LayerFlags;
use crate::tables::linetype::Linetype;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::transparency::Transparency;

/// Represents a DXF layer (`LAYER`).
#[derive(Clone, Debug, PartialEq)]
pub struct Layer {
    pub table: TableObjectData,
    pub color: AciColor,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_frozen_in_new_viewports: bool,
    pub is_locked: bool,
    pub plot: bool,
    pub linetype: String,
    pub lineweight: Lineweight,
    pub transparency: Transparency,
    pub description: String,
}

impl Layer {
    pub const DEFAULT_NAME: &'static str = "0";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::LAYER, true)?,
            color: AciColor::DEFAULT,
            is_visible: true,
            is_frozen: false,
            is_frozen_in_new_viewports: false,
            is_locked: false,
            plot: true,
            linetype: Linetype::CONTINUOUS_NAME.to_string(),
            lineweight: Lineweight::Default,
            transparency: Transparency::from_cad_index(0),
            description: String::new(),
        })
    }

    pub fn default_layer() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::LAYER),
            color: AciColor::DEFAULT,
            is_visible: true,
            is_frozen: false,
            is_frozen_in_new_viewports: false,
            is_locked: false,
            plot: true,
            linetype: Linetype::CONTINUOUS_NAME.to_string(),
            lineweight: Lineweight::Default,
            transparency: Transparency::from_cad_index(0),
            description: String::new(),
        }
    }

    pub fn flags(&self) -> LayerFlags {
        let mut f = LayerFlags::NONE;
        if self.is_frozen {
            f |= LayerFlags::FROZEN;
        }
        if self.is_locked {
            f |= LayerFlags::LOCKED;
        }
        f
    }
}

impl Default for Layer {
    fn default() -> Self {
        Self::default_layer()
    }
}

impl TableObject for Layer {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
