use crate::entities::AttributeMText;
use crate::vector3::Vector3;

/// Stored text and optional multiline `MTEXT` data for an `Attribute` or `AttributeDefinition`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct AttributeTextData {
    pub kind: i16,
    pub content: String,
    pub text: String,
    pub mtext: Option<AttributeMText>,
    pub version: i16,
    pub field_length: i16,
    pub is_multiline: bool,
    pub locked: bool,
    pub lock_position_in_block: bool,
    pub secondary_flag: i16,
    pub secondary_alignment: Option<Vector3>,
    pub auxiliary: Vec<(i32, String)>,
}
