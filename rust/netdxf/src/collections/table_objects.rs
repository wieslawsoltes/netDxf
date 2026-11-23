use indexmap::IndexMap;

use crate::error::{DxfError, Result};
use crate::tables::TableObject;

/// Case-insensitive, insertion-ordered DXF table collection (`netDxf.Collections.TableObjects<T>`).
#[derive(Clone, Debug, PartialEq)]
pub struct TableObjects<T: TableObject> {
    items: IndexMap<String, T>,
    kind: &'static str,
}

impl<T: TableObject> TableObjects<T> {
    pub fn new(kind: &'static str) -> Self {
        Self {
            items: IndexMap::new(),
            kind,
        }
    }

    #[inline]
    fn key(name: &str) -> String {
        name.to_uppercase()
    }

    /// Adds a table object if its name does not already exist; returns a reference to the stored entry.
    pub fn add(&mut self, item: T) -> &T {
        let k = Self::key(item.name());
        self.items.entry(k).or_insert(item)
    }

    /// Inserts or replaces a table object by name.
    pub fn insert(&mut self, item: T) -> Option<T> {
        let k = Self::key(item.name());
        self.items.insert(k, item)
    }

    #[inline]
    pub fn contains(&self, name: &str) -> bool {
        self.items.contains_key(&Self::key(name))
    }

    #[inline]
    pub fn get(&self, name: &str) -> Option<&T> {
        self.items.get(&Self::key(name))
    }

    #[inline]
    pub fn get_mut(&mut self, name: &str) -> Option<&mut T> {
        self.items.get_mut(&Self::key(name))
    }

    pub fn remove(&mut self, name: &str) -> Result<T> {
        let k = Self::key(name);
        if let Some(existing) = self.items.get(&k) {
            if existing.is_reserved() {
                return Err(DxfError::CannotRemove {
                    kind: self.kind,
                    name: name.to_string(),
                    reason: "reserved table objects cannot be removed".to_string(),
                });
            }
        }
        self.items.shift_remove(&k).ok_or_else(|| DxfError::NotFound {
            kind: self.kind,
            name: name.to_string(),
        })
    }

    #[inline]
    pub fn len(&self) -> usize {
        self.items.len()
    }

    #[inline]
    pub fn is_empty(&self) -> bool {
        self.items.is_empty()
    }

    pub fn iter(&self) -> impl Iterator<Item = &T> {
        self.items.values()
    }

    pub fn iter_mut(&mut self) -> impl Iterator<Item = &mut T> {
        self.items.values_mut()
    }

    pub fn names(&self) -> impl Iterator<Item = &str> {
        self.items.values().map(|v| v.name())
    }

    #[inline]
    pub fn count(&self) -> usize {
        self.items.len()
    }

    pub fn items(&self) -> Vec<&T> {
        self.items.values().collect()
    }

    #[inline]
    pub fn try_get_value(&self, name: &str) -> Option<&T> {
        self.get(name)
    }

    pub fn clear(&mut self) {
        self.items.retain(|_, v| v.is_reserved());
    }

    #[inline]
    pub fn has_references(&self, name: &str) -> bool {
        self.get(name).is_some_and(|v| v.has_references())
    }

    #[inline]
    pub fn get_references(&self, name: &str) -> Vec<String> {
        self.get(name)
            .map_or_else(Vec::new, |v| v.get_references())
    }

    #[inline]
    pub fn owner(&self) -> &'static str {
        self.kind
    }
}
