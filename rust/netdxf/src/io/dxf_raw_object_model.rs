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

    pub fn owner_handle(&self) -> u64 {
        match self {
            Self::Dictionary(o) => o.owner_handle,
            Self::DictionaryVariable(o) => o.owner_handle,
            Self::IdBuffer(o) => o.owner_handle,
            Self::Placeholder(o) => o.owner_handle,
            Self::SortentsTable(o) => o.owner_handle,
            Self::XRecord(o) => o.owner_handle,
            Self::Opaque(o) => o.owner_handle,
        }
    }

    pub fn set_owner_handle(&mut self, owner: u64) {
        match self {
            Self::Dictionary(o) => o.owner_handle = owner,
            Self::DictionaryVariable(o) => o.owner_handle = owner,
            Self::IdBuffer(o) => o.owner_handle = owner,
            Self::Placeholder(o) => o.owner_handle = owner,
            Self::SortentsTable(o) => o.owner_handle = owner,
            Self::XRecord(o) => o.owner_handle = owner,
            Self::Opaque(o) => o.owner_handle = owner,
        }
    }
}

/// In-memory indexed store of raw DXF `OBJECTS` section records.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfRawObjectStore {
    pub options: DxfRawObjectStoreOptions,
    pub objects: BTreeMap<u64, DxfRawStoredObject>,
    pub root_dictionary_handle: Option<u64>,
    pub next_handle_seed: u64,
}

impl DxfRawObjectStore {
    pub fn new(options: DxfRawObjectStoreOptions) -> Self {
        Self {
            options,
            objects: BTreeMap::new(),
            root_dictionary_handle: None,
            next_handle_seed: 1,
        }
    }

    pub fn insert(&mut self, obj: DxfRawStoredObject) {
        let h = obj.handle();
        if h >= self.next_handle_seed {
            self.next_handle_seed = h + 1;
        }
        self.objects.insert(h, obj);
    }

    pub fn get(&self, handle: u64) -> Option<&DxfRawStoredObject> {
        self.objects.get(&handle)
    }

    pub fn begin_transaction(&self) -> DxfRawObjectTransaction {
        DxfRawObjectTransaction {
            staged: self.objects.clone(),
            root_dictionary_handle: self.root_dictionary_handle,
            next_handle_seed: self.next_handle_seed.max(1),
        }
    }
}

/// Atomic staging transaction over a [`DxfRawObjectStore`].
#[derive(Debug, Clone, PartialEq)]
pub struct DxfRawObjectTransaction {
    staged: BTreeMap<u64, DxfRawStoredObject>,
    root_dictionary_handle: Option<u64>,
    next_handle_seed: u64,
}

impl DxfRawObjectTransaction {
    pub fn allocate_handle(&mut self) -> u64 {
        while self.staged.contains_key(&self.next_handle_seed) || self.next_handle_seed == 0 {
            self.next_handle_seed += 1;
        }
        let h = self.next_handle_seed;
        self.next_handle_seed += 1;
        h
    }

    pub fn get(&self, handle: u64) -> Option<&DxfRawStoredObject> {
        self.staged.get(&handle)
    }

    pub fn get_mut(&mut self, handle: u64) -> Option<&mut DxfRawStoredObject> {
        self.staged.get_mut(&handle)
    }

    pub fn upsert(&mut self, obj: DxfRawStoredObject) {
        let h = obj.handle();
        if h >= self.next_handle_seed {
            self.next_handle_seed = h + 1;
        }
        self.staged.insert(h, obj);
    }

    pub fn remove(&mut self, handle: u64) -> Option<DxfRawStoredObject> {
        self.staged.remove(&handle)
    }

    /// Ensures a root `DICTIONARY` exists and returns its handle.
    pub fn ensure_root_dictionary(&mut self) -> u64 {
        if let Some(h) = self.root_dictionary_handle {
            if self.staged.contains_key(&h) {
                return h;
            }
        }
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::Dictionary(DxfRawDictionary {
                handle: h,
                owner_handle: 0,
                is_hard_owner: false,
                cloning: DxfDuplicateRecordCloning::KeepExisting,
                entries: Vec::new(),
            }),
        );
        self.root_dictionary_handle = Some(h);
        h
    }

    /// Ensures an extension `DICTIONARY` owned by `owner_handle` exists and returns its handle.
    pub fn ensure_extension_dictionary(&mut self, owner_handle: u64) -> u64 {
        for obj in self.staged.values() {
            if let DxfRawStoredObject::Dictionary(dict) = obj {
                if dict.owner_handle == owner_handle {
                    return dict.handle;
                }
            }
        }
        self.create_dictionary(owner_handle, DxfDuplicateRecordCloning::KeepExisting)
    }

    /// Creates a new `DICTIONARY` owned by `owner_handle`.
    pub fn create_dictionary(
        &mut self,
        owner_handle: u64,
        cloning: DxfDuplicateRecordCloning,
    ) -> u64 {
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::Dictionary(DxfRawDictionary {
                handle: h,
                owner_handle,
                is_hard_owner: false,
                cloning,
                entries: Vec::new(),
            }),
        );
        h
    }

    /// Creates a new `XRECORD` owned by `owner_handle`.
    pub fn create_xrecord(&mut self, owner_handle: u64, tags: Vec<DxfTag>) -> u64 {
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::XRecord(DxfRawXRecord {
                handle: h,
                owner_handle,
                cloning: DxfDuplicateRecordCloning::KeepExisting,
                tags,
            }),
        );
        h
    }

    /// Creates a new `IDBUFFER` owned by `owner_handle`.
    pub fn create_id_buffer(&mut self, owner_handle: u64, object_handles: Vec<u64>) -> u64 {
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::IdBuffer(DxfRawIdBuffer {
                handle: h,
                owner_handle,
                object_handles,
            }),
        );
        h
    }

    /// Creates a new `ACDBPLACEHOLDER` owned by `owner_handle`.
    pub fn create_placeholder(&mut self, owner_handle: u64) -> u64 {
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::Placeholder(DxfRawPlaceholder {
                handle: h,
                owner_handle,
            }),
        );
        h
    }

    /// Creates a new `DICTIONARYVAR` owned by `owner_handle`.
    pub fn create_variable(&mut self, owner_handle: u64, value: impl Into<String>) -> u64 {
        let h = self.allocate_handle();
        self.staged.insert(
            h,
            DxfRawStoredObject::DictionaryVariable(DxfRawDictionaryVariable {
                handle: h,
                owner_handle,
                schema_number: 0,
                value: value.into(),
            }),
        );
        h
    }

    /// Inserts or updates a named entry in `dictionary_handle`.
    pub fn set_dictionary_entry(
        &mut self,
        dictionary_handle: u64,
        name: impl Into<String>,
        target_handle: u64,
    ) -> bool {
        let key = name.into();
        if let Some(DxfRawStoredObject::Dictionary(dict)) = self.staged.get_mut(&dictionary_handle)
        {
            if let Some(existing) = dict
                .entries
                .iter_mut()
                .find(|e| e.name.eq_ignore_ascii_case(&key))
            {
                existing.target_handle = target_handle;
            } else {
                dict.entries.push(DxfRawDictionaryEntry {
                    name: key,
                    target_handle,
                });
            }
            return true;
        }
        false
    }

    /// Deep-clones a dictionary subtree under `new_owner_handle`, allocating fresh handles for all owned descendants.
    pub fn clone_dictionary_tree(&mut self, source_handle: u64, new_owner_handle: u64) -> Option<u64> {
        let src = self.staged.get(&source_handle)?.clone();
        let new_h = self.allocate_handle();
        match src {
            DxfRawStoredObject::Dictionary(mut dict) => {
                dict.handle = new_h;
                dict.owner_handle = new_owner_handle;
                let mut cloned_entries = Vec::with_capacity(dict.entries.len());
                for entry in dict.entries {
                    let child_new_h = self
                        .clone_dictionary_tree(entry.target_handle, new_h)
                        .unwrap_or(entry.target_handle);
                    cloned_entries.push(DxfRawDictionaryEntry {
                        name: entry.name,
                        target_handle: child_new_h,
                    });
                }
                dict.entries = cloned_entries;
                self.staged.insert(new_h, DxfRawStoredObject::Dictionary(dict));
                Some(new_h)
            }
            mut other => {
                other.set_owner_handle(new_owner_handle);
                let cloned = match other {
                    DxfRawStoredObject::DictionaryVariable(mut v) => {
                        v.handle = new_h;
                        DxfRawStoredObject::DictionaryVariable(v)
                    }
                    DxfRawStoredObject::IdBuffer(mut b) => {
                        b.handle = new_h;
                        DxfRawStoredObject::IdBuffer(b)
                    }
                    DxfRawStoredObject::Placeholder(mut p) => {
                        p.handle = new_h;
                        DxfRawStoredObject::Placeholder(p)
                    }
                    DxfRawStoredObject::SortentsTable(mut s) => {
                        s.handle = new_h;
                        DxfRawStoredObject::SortentsTable(s)
                    }
                    DxfRawStoredObject::XRecord(mut x) => {
                        x.handle = new_h;
                        DxfRawStoredObject::XRecord(x)
                    }
                    DxfRawStoredObject::Opaque(mut o) => {
                        o.handle = new_h;
                        DxfRawStoredObject::Opaque(o)
                    }
                    DxfRawStoredObject::Dictionary(_) => unreachable!(),
                };
                self.staged.insert(new_h, cloned);
                Some(new_h)
            }
        }
    }

    /// Recursively deletes `root_handle` and all objects owned by it.
    pub fn delete_owned_tree(&mut self, root_handle: u64) -> usize {
        let mut stack = vec![root_handle];
        let mut removed = 0usize;
        while let Some(h) = stack.pop() {
            if self.staged.remove(&h).is_some() {
                removed += 1;
                let children: Vec<u64> = self
                    .staged
                    .values()
                    .filter(|o| o.owner_handle() == h)
                    .map(|o| o.handle())
                    .collect();
                stack.extend(children);
            }
        }
        removed
    }

    pub fn commit(self, store: &mut DxfRawObjectStore) {
        store.objects = self.staged;
        store.root_dictionary_handle = self.root_dictionary_handle;
        store.next_handle_seed = self.next_handle_seed;
    }
}
