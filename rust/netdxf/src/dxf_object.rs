use indexmap::IndexMap;

use crate::x_data::XData;

/// Represents the base metadata for all DXF objects (mirrors `netDxf.DxfObject`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfObject {
    pub code_name: String,
    pub handle: Option<String>,
    pub owner_handle: Option<String>,
    pub x_data: IndexMap<String, XData>,
    pub persistent_reactors: Vec<String>,
}

impl DxfObject {
    pub fn new(code_name: impl Into<String>) -> Self {
        Self {
            code_name: code_name.into(),
            handle: None,
            owner_handle: None,
            x_data: IndexMap::new(),
            persistent_reactors: Vec::new(),
        }
    }

    /// Assigns a hexadecimal handle from `entity_number` and returns the next available number.
    pub fn assign_handle(&mut self, entity_number: u64) -> u64 {
        self.handle = Some(format!("{entity_number:X}"));
        entity_number + 1
    }

    pub fn add_xdata(&mut self, xdata: XData) {
        let key = xdata.application_registry.to_uppercase();
        self.x_data.insert(key, xdata);
    }

    pub fn get_xdata(&self, app_reg: &str) -> Option<&XData> {
        self.x_data.get(&app_reg.to_uppercase())
    }

    pub fn remove_xdata(&mut self, app_reg: &str) -> Option<XData> {
        self.x_data.shift_remove(&app_reg.to_uppercase())
    }
}
