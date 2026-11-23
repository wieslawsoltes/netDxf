use crate::entities::Attribute;

/// Describes a replacement attribute operation on an `Insert` entity.
#[derive(Debug, Clone, PartialEq)]
pub struct InsertAttributeReplacement {
    pub tag: String,
    pub replacement: Attribute,
}
