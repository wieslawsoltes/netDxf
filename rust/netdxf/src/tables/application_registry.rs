use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};

/// Represents a registered application name (`APPID`) to which extended data is associated.
#[derive(Clone, Debug, PartialEq)]
pub struct ApplicationRegistry {
    pub table: TableObjectData,
}

impl ApplicationRegistry {
    pub const DEFAULT_NAME: &'static str = "ACAD";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::APP_ID, true)?,
        })
    }

    pub fn default_app_reg() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::APP_ID),
        }
    }

    #[allow(clippy::should_implement_trait)]
    pub fn default() -> Self {
        Self::default_app_reg()
    }
}

impl TableObject for ApplicationRegistry {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
