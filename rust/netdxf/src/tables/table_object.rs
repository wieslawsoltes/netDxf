use crate::dxf_object::DxfObject;
use crate::error::{DxfError, Result};

/// Characters not supported in DXF table object names.
pub const INVALID_CHARACTERS: &[char] = &[
    '\\', '/', ':', '*', '?', '"', '<', '>', '|', ';', ',', '=', '`',
];

/// Validates a DXF table object name according to AutoCAD rules.
pub fn is_valid_name(name: &str) -> bool {
    let trimmed = name.trim();
    !trimmed.is_empty() && !trimmed.chars().any(|c| INVALID_CHARACTERS.contains(&c))
}

/// Shared data for all DXF table objects (`netDxf.Tables.TableObject`).
#[derive(Clone, Debug, PartialEq)]
pub struct TableObjectData {
    pub object: DxfObject,
    pub name: String,
    pub is_reserved: bool,
}

impl TableObjectData {
    pub fn new(name: impl Into<String>, code_name: &'static str, check_name: bool) -> Result<Self> {
        let raw = name.into();
        let trimmed = raw.trim();
        if check_name && !is_valid_name(trimmed) {
            return Err(DxfError::InvalidName {
                name: raw,
                reason: "The name should be at least one character long and must not contain \\<>/?\":;*|,=`".to_string(),
            });
        }
        Ok(Self {
            object: DxfObject::new(code_name),
            name: trimmed.to_string(),
            is_reserved: false,
        })
    }

    pub fn reserved(name: impl Into<String>, code_name: &'static str) -> Self {
        Self {
            object: DxfObject::new(code_name),
            name: name.into(),
            is_reserved: true,
        }
    }
}

/// Trait implemented by all DXF table objects stored in `TableObjects<T>`.
pub trait TableObject {
    const INVALID_CHARACTERS: &'static [char] = INVALID_CHARACTERS;

    fn table_data(&self) -> &TableObjectData;
    fn table_data_mut(&mut self) -> &mut TableObjectData;

    #[inline]
    fn is_valid_name(name: &str) -> bool
    where
        Self: Sized,
    {
        is_valid_name(name)
    }

    #[inline]
    fn name(&self) -> &str {
        &self.table_data().name
    }

    #[inline]
    fn is_reserved(&self) -> bool {
        self.table_data().is_reserved
    }

    #[inline]
    fn handle(&self) -> Option<&str> {
        self.table_data().object.handle.as_deref()
    }

    #[inline]
    fn has_references(&self) -> bool {
        false
    }

    #[inline]
    fn get_references(&self) -> Vec<String> {
        Vec::new()
    }
}
