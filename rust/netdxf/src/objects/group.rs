use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::{TableObject, TableObjectData};

/// Represents a named or unnamed `GROUP` of entity handles (`OBJECTS` section).
#[derive(Clone, Debug, PartialEq)]
pub struct Group {
    pub table: TableObjectData,
    pub description: String,
    pub is_selectable: bool,
    pub is_unnamed: bool,
    pub entity_handles: Vec<String>,
}

impl Group {
    pub fn new(name: impl Into<String>) -> Result<Self> {
        let raw = name.into();
        let is_unnamed = raw.is_empty() || raw.starts_with('*');
        let table = if is_unnamed {
            TableObjectData::new(raw, DxfObjectCode::GROUP, false)?
        } else {
            TableObjectData::new(raw, DxfObjectCode::GROUP, true)?
        };
        Ok(Self {
            table,
            description: String::new(),
            is_selectable: true,
            is_unnamed,
            entity_handles: Vec::new(),
        })
    }
}

impl TableObject for Group {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
