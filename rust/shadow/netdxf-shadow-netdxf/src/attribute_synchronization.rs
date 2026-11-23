use netdxf_shadow::ObjectId;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AttributeSynchronizationOptions {
    pub remove_orphan_attributes: bool,
    pub preserve_existing_values: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AttributeSynchronizationRemoval {
    pub insert_id: ObjectId,
    pub attribute_id: ObjectId,
    pub tag: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AttributeSynchronizationReference {
    pub insert_id: ObjectId,
    pub definition_id: ObjectId,
    pub tag: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AttributeSynchronizationPlan {
    pub additions: Vec<AttributeSynchronizationReference>,
    pub removals: Vec<AttributeSynchronizationRemoval>,
}

pub struct AttributeSynchronization;

impl AttributeSynchronization {
    pub fn plan(options: AttributeSynchronizationOptions) -> AttributeSynchronizationPlan {
        let _ = options;
        AttributeSynchronizationPlan::default()
    }
}
