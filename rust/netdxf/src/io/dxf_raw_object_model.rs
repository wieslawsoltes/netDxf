use crate::io::{DxfRawRecord, DxfTag};
use std::collections::BTreeMap;

/// Duplicate record cloning policy stored on DXF `DICTIONARY` objects (group 281).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum DxfDuplicateRecordCloning {
    #[default]
    NotApplicable = 0,
    KeepExisting = 1,
    UseClone = 2,
    XrefMangleName = 3,
    MangleName = 4,
    UnmangleName = 5,
}

/// Resource limits for [`DxfRawObjectStore`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DxfRawObjectStoreOptions {
    pub maximum_objects: usize,
    pub maximum_tags_per_object: usize,
}

impl Default for DxfRawObjectStoreOptions {
    fn default() -> Self {
        Self {
            maximum_objects: 1_000_000,
            maximum_tags_per_object: 1_000_000,
        }
    }
}

/// Single entry inside a [`DxfRawDictionary`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfRawDictionaryEntry {
    pub name: String,
    pub target_handle: u64,
}

/// Raw `DICTIONARY` stored object representation.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawDictionary {
    pub handle: u64,
    pub owner_handle: u64,
    pub is_hard_owner: bool,
    pub cloning: DxfDuplicateRecordCloning,
    pub entries: Vec<DxfRawDictionaryEntry>,
}

/// Raw `DICTIONARYVAR` stored object representation.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawDictionaryVariable {
    pub handle: u64,
    pub owner_handle: u64,
    pub schema_number: i16,
    pub value: String,
}

/// Raw `IDBUFFER` stored object representation.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawIdBuffer {
    pub handle: u64,
    pub owner_handle: u64,
    pub object_handles: Vec<u64>,
}

/// Raw `ACDBPLACEHOLDER` stored object representation.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawPlaceholder {
    pub handle: u64,
    pub owner_handle: u64,
}

/// Single sort order entry inside a [`DxfRawSortentsTable`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfRawSortOrderEntry {
    pub entity_handle: u64,
    pub sort_handle: u64,
}

/// Raw `SORTENTSTABLE` stored object representation.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawSortentsTable {
    pub handle: u64,
    pub owner_handle: u64,
    pub block_record_handle: u64,
    pub entries: Vec<DxfRawSortOrderEntry>,
}

/// Raw `XRECORD` stored object representation.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfRawXRecord {
    pub handle: u64,
    pub owner_handle: u64,
    pub cloning: DxfDuplicateRecordCloning,
    pub tags: Vec<DxfTag>,
}

/// Raw opaque stored object preserving arbitrary tags verbatim.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfRawOpaqueStoredObject {
    pub handle: u64,
    pub owner_handle: u64,
    pub record: DxfRawRecord,
}

/// Polymorphic raw stored object inside [`DxfRawObjectStore`].
#[derive(Debug, Clone, PartialEq)]
pub enum DxfRawStoredObject {
    Dictionary(DxfRawDictionary),
    DictionaryVariable(DxfRawDictionaryVariable),
    IdBuffer(DxfRawIdBuffer),
    Placeholder(DxfRawPlaceholder),
    SortentsTable(DxfRawSortentsTable),
    XRecord(DxfRawXRecord),
    Opaque(DxfRawOpaqueStoredObject),
}

impl DxfRawStoredObject {
    pub fn handle(&self) -> u64 {
        match self {
            Self::Dictionary(o) => o.handle,
            Self::DictionaryVariable(o) => o.handle,
            Self::IdBuffer(o) => o.handle,
            Self::Placeholder(o) => o.handle,
            Self::SortentsTable(o) => o.handle,
            Self::XRecord(o) => o.handle,
            Self::Opaque(o) => o.handle,
        }
    }
}

/// In-memory indexed store of raw DXF `OBJECTS` section records.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfRawObjectStore {
    pub options: DxfRawObjectStoreOptions,
    pub objects: BTreeMap<u64, DxfRawStoredObject>,
}

impl DxfRawObjectStore {
    pub fn new(options: DxfRawObjectStoreOptions) -> Self {
        Self {
            options,
            objects: BTreeMap::new(),
        }
    }

    pub fn insert(&mut self, obj: DxfRawStoredObject) {
        self.objects.insert(obj.handle(), obj);
    }

    pub fn get(&self, handle: u64) -> Option<&DxfRawStoredObject> {
        self.objects.get(&handle)
    }

    pub fn begin_transaction(&self) -> DxfRawObjectTransaction {
        DxfRawObjectTransaction {
            staged: self.objects.clone(),
        }
    }
}

/// Atomic staging transaction over a [`DxfRawObjectStore`].
#[derive(Debug, Clone, PartialEq)]
pub struct DxfRawObjectTransaction {
    staged: BTreeMap<u64, DxfRawStoredObject>,
}

impl DxfRawObjectTransaction {
    pub fn upsert(&mut self, obj: DxfRawStoredObject) {
        self.staged.insert(obj.handle(), obj);
    }

    pub fn remove(&mut self, handle: u64) -> Option<DxfRawStoredObject> {
        self.staged.remove(&handle)
    }

    pub fn commit(self, store: &mut DxfRawObjectStore) {
        store.objects = self.staged;
    }
}
