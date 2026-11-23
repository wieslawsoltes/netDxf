use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;

/// Raw DXF `(group_code, value)` tag for opaque/round-tripped entities.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfTag {
    pub code: i16,
    pub value: String,
}

impl DxfTag {
    pub fn new(code: i16, value: impl Into<String>) -> Self {
        Self {
            code,
            value: value.into(),
        }
    }
}

/// Preserves unsupported or custom DXF entities losslessly across read/write round trips.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfOpaqueEntity {
    pub common: EntityCommon,
    pub entity_name: String,
    pub raw_tags: Vec<DxfTag>,
}

impl DxfOpaqueEntity {
    pub fn new(entity_name: impl Into<String>, raw_tags: Vec<DxfTag>) -> Self {
        let name = entity_name.into();
        let mut common = EntityCommon::new(EntityType::Opaque, "");
        common.object.code_name = name.clone();
        Self {
            common,
            entity_name: name,
            raw_tags,
        }
    }
}
