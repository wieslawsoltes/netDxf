use crate::entities::AttributeMText;

/// Stored text and optional multiline `MTEXT` data for an `Attribute` or `AttributeDefinition`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct AttributeTextData {
    pub text: String,
    pub mtext: Option<AttributeMText>,
    pub is_multiline: bool,
    pub lock_position_in_block: bool,
}
