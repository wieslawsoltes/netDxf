use crate::dxf_object_code::DxfObjectCode;
use crate::entities::UnderlayType;
use crate::error::Result;
use crate::tables::{TableObject, TableObjectData};

/// Represents a DXF `PDFDEFINITION` / `DWFDEFINITION` / `DGNDEFINITION` object.
#[derive(Clone, Debug, PartialEq)]
pub struct UnderlayDefinition {
    pub table: TableObjectData,
    pub underlay_type: UnderlayType,
    pub file: String,
    pub item_name: String,
}

impl UnderlayDefinition {
    pub fn new(
        underlay_type: UnderlayType,
        name: impl Into<String>,
        file: impl Into<String>,
    ) -> Result<Self> {
        let code = match underlay_type {
            UnderlayType::DGN => DxfObjectCode::UNDERLAY_DGN_DEFINITION,
            UnderlayType::DWF => DxfObjectCode::UNDERLAY_DWF_DEFINITION,
            UnderlayType::PDF => DxfObjectCode::UNDERLAY_PDF_DEFINITION,
        };
        Ok(Self {
            table: TableObjectData::new(name, code, true)?,
            underlay_type,
            file: file.into(),
            item_name: "1".to_string(),
        })
    }
}

impl TableObject for UnderlayDefinition {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
