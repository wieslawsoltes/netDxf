use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct InsertAttributeAssignment {
    pub tag: String,
    pub value: String,
}

impl InsertAttributeAssignment {
    pub fn definition(&self) -> &str {
        &self.tag
    }
}

pub struct InsertAttributeConstruction;

impl InsertAttributeConstruction {
    pub fn assignment(tag: impl Into<String>, value: impl Into<String>) -> InsertAttributeAssignment {
        InsertAttributeAssignment {
            tag: tag.into(),
            value: value.into(),
        }
    }

    pub fn plan(assignments: &[InsertAttributeAssignment]) -> Vec<InsertAttributeAssignment> {
        assignments.to_vec()
    }
}
