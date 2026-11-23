use crate::x_data_record::XDataRecord;

/// Represents extended data (XData) associated with an application registry name.
#[derive(Clone, Debug, PartialEq)]
pub struct XData {
    pub application_registry: String,
    pub x_data_record: Vec<XDataRecord>,
}

impl XData {
    pub fn new(application_registry: impl Into<String>) -> Self {
        Self {
            application_registry: application_registry.into(),
            x_data_record: Vec::new(),
        }
    }

    pub fn with_records(application_registry: impl Into<String>, records: Vec<XDataRecord>) -> Self {
        Self {
            application_registry: application_registry.into(),
            x_data_record: records,
        }
    }
}
