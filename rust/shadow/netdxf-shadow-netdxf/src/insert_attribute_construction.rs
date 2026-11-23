use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct InsertAttributeAssignment {
    pub tag: String,
    pub value: String,
}

pub struct InsertAttributeConstruction;

impl InsertAttributeConstruction {
    pub fn assignment(tag: impl Into<String>, value: impl Into<String>) -> InsertAttributeAssignment {
        InsertAttributeAssignment {
            tag: tag.into(),
            value: value.into(),
        }
    }
}
