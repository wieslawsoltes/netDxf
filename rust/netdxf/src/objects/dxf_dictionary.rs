use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::objects::DictionaryCloningFlags;

/// Single key/handle entry inside a [`DxfDictionary`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfDictionaryEntry {
    pub name: String,
    pub target_handle: String,
}

/// First-class DXF `DICTIONARY` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfDictionary {
    pub object: DxfObject,
    pub is_hard_owner: bool,
    pub cloning_flags: DictionaryCloningFlags,
    pub entries: Vec<DxfDictionaryEntry>,
}

impl Default for DxfDictionary {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfDictionary {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::DICTIONARY),
            is_hard_owner: false,
            cloning_flags: DictionaryCloningFlags::KeepExisting,
            entries: Vec::new(),
        }
    }
}

/// First-class DXF `ACDBDICTIONARYWDFLT` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfDictionaryWithDefault {
    pub dictionary: DxfDictionary,
    pub default_entry_handle: Option<String>,
}

impl Default for DxfDictionaryWithDefault {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfDictionaryWithDefault {
    pub fn new() -> Self {
        Self {
            dictionary: DxfDictionary {
                object: DxfObject::new("ACDBDICTIONARYWDFLT"),
                is_hard_owner: false,
                cloning_flags: DictionaryCloningFlags::KeepExisting,
                entries: Vec::new(),
            },
            default_entry_handle: None,
        }
    }
}

/// First-class DXF `DICTIONARYVAR` database object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfDictionaryVariable {
    pub object: DxfObject,
    pub schema_number: i16,
    pub value: String,
}

impl Default for DxfDictionaryVariable {
    fn default() -> Self {
        Self::new("")
    }
}

impl DxfDictionaryVariable {
    pub fn new(value: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new("DICTIONARYVAR"),
            schema_number: 0,
            value: value.into(),
        }
    }
}
