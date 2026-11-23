use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::objects::DictionaryCloningFlags;

/// Single key/handle entry inside a [`DxfDictionary`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfDictionaryEntry {
    pub name: String,
    pub target_handle: String,
}

impl DxfDictionaryEntry {
    pub fn create_unresolved_soft_reference(
        name: impl Into<String>,
        target_handle: impl Into<String>,
    ) -> Self {
        Self {
            name: name.into(),
            target_handle: target_handle.into(),
        }
    }

    #[inline]
    pub fn is_hard_owner(&self) -> bool {
        false
    }

    #[inline]
    pub fn is_resolved(&self) -> bool {
        !self.target_handle.is_empty()
    }

    #[inline]
    pub fn target(&self) -> &str {
        &self.target_handle
    }
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

    #[inline]
    pub fn cloning(&self) -> DictionaryCloningFlags {
        self.cloning_flags
    }

    #[inline]
    pub fn count(&self) -> usize {
        self.entries.len()
    }

    pub fn add(&mut self, name: impl Into<String>, target_handle: impl Into<String>) -> bool {
        let key = name.into();
        if self.contains(&key) {
            return false;
        }
        self.entries.push(DxfDictionaryEntry {
            name: key,
            target_handle: target_handle.into(),
        });
        true
    }

    pub fn add_unresolved_soft_reference(
        &mut self,
        name: impl Into<String>,
        target_handle: impl Into<String>,
    ) -> bool {
        self.add(name, target_handle)
    }

    pub fn contains(&self, name: &str) -> bool {
        self.entries.iter().any(|e| e.name.eq_ignore_ascii_case(name))
    }

    pub fn try_get_value(&self, name: &str) -> Option<&str> {
        self.entries
            .iter()
            .find(|e| e.name.eq_ignore_ascii_case(name))
            .map(|e| e.target_handle.as_str())
    }

    pub fn remove(&mut self, name: &str) -> bool {
        if let Some(idx) = self
            .entries
            .iter()
            .position(|e| e.name.eq_ignore_ascii_case(name))
        {
            self.entries.remove(idx);
            true
        } else {
            false
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
