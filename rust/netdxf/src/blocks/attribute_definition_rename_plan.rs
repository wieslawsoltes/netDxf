use crate::blocks::Block;
use crate::error::{DxfError, Result};
use indexmap::IndexMap;
use std::collections::{HashMap, HashSet};

/// A single-use validated rename plan bound to one block's current attribute definition membership and order.
#[derive(Debug, Clone)]
pub struct AttributeDefinitionRenamePlan {
    expected_tags: Vec<String>,
    new_tags: Vec<String>,
    applied: bool,
}

impl AttributeDefinitionRenamePlan {
    /// Prepares simultaneous attribute definition tag changes, including swaps, without replacing definitions.
    pub fn prepare(block: &Block, renames: &[(String, String)]) -> Result<Self> {
        let mut rename_map = HashMap::new();
        for (old_tag, new_tag) in renames {
            if new_tag.trim().is_empty() {
                return Err(DxfError::InvalidValue(
                    "Attribute definition tag cannot be empty.".into(),
                ));
            }
            let key = old_tag.to_ascii_uppercase();
            if rename_map.insert(key, new_tag.clone()).is_some() {
                return Err(DxfError::InvalidValue(format!(
                    "Duplicate rename source tag '{old_tag}'."
                )));
            }
        }

        let expected_tags: Vec<String> = block
            .attribute_definitions
            .values()
            .map(|d| d.tag.clone())
            .collect();
        let mut new_tags = Vec::with_capacity(expected_tags.len());
        let mut used = HashSet::new();
        let mut matched_sources = 0usize;

        for tag in &expected_tags {
            let key = tag.to_ascii_uppercase();
            let target = if let Some(new_tag) = rename_map.get(&key) {
                matched_sources += 1;
                new_tag.clone()
            } else {
                tag.clone()
            };
            if !used.insert(target.to_ascii_uppercase()) {
                return Err(DxfError::InvalidValue(format!(
                    "Duplicate resulting attribute definition tag '{target}'."
                )));
            }
            new_tags.push(target);
        }

        if matched_sources != rename_map.len() {
            return Err(DxfError::InvalidValue(
                "One or more rename source tags were not found in the block.".into(),
            ));
        }

        Ok(Self {
            expected_tags,
            new_tags,
            applied: false,
        })
    }

    /// Applies the prepared tags, or refuses a changed block without modifying it.
    pub fn apply(&mut self, block: &mut Block) -> Result<()> {
        if self.applied {
            return Err(DxfError::InvalidValue(
                "AttributeDefinitionRenamePlan has already been applied.".into(),
            ));
        }
        let current_tags: Vec<String> = block
            .attribute_definitions
            .values()
            .map(|d| d.tag.clone())
            .collect();
        if current_tags != self.expected_tags {
            return Err(DxfError::InvalidValue(
                "Block attribute definitions changed after rename plan was prepared.".into(),
            ));
        }
        let mut rebuilt = IndexMap::with_capacity(block.attribute_definitions.len());
        for ((_, mut def), new_tag) in block
            .attribute_definitions
            .drain(..)
            .zip(self.new_tags.iter())
        {
            def.tag = new_tag.clone();
            rebuilt.insert(new_tag.clone(), def);
        }
        block.attribute_definitions = rebuilt;
        self.applied = true;
        Ok(())
    }
}
