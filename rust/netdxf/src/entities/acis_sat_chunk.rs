use crate::error::{DxfError, Result};

/// An encoded DXF ACIS SAT line chunk (group code 1 or 3, up to 255 printable ASCII chars).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AcisSatChunk {
    pub group_code: i16,
    pub text: String,
}

impl AcisSatChunk {
    pub const MAXIMUM_LENGTH: usize = 255;

    pub fn new(group_code: i16, text: impl Into<String>) -> Result<Self> {
        if group_code != 1 && group_code != 3 {
            return Err(DxfError::InvalidValue(format!(
                "ACIS SAT chunk group code must be 1 or 3, got {group_code}"
            )));
        }
        let text = text.into();
        if text.len() > Self::MAXIMUM_LENGTH {
            return Err(DxfError::InvalidValue(format!(
                "ACIS SAT chunk exceeds 255 characters ({})",
                text.len()
            )));
        }
        if !text.bytes().all(|b| (32..=126).contains(&b)) {
            return Err(DxfError::InvalidValue(
                "ACIS SAT chunk must contain printable ASCII without newlines".to_string(),
            ));
        }
        Ok(Self { group_code, text })
    }
}
