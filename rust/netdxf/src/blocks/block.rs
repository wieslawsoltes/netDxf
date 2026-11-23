use indexmap::IndexMap;

use crate::blocks::block_record::BlockRecord;
use crate::blocks::block_type_flags::BlockTypeFlags;
use crate::blocks::end_block::EndBlock;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::{AttributeDefinition, EntityObject};
use crate::error::Result;
use crate::tables::{Layer, TableObject, TableObjectData};
use crate::vector3::Vector3;

/// Represents a DXF `BLOCK` definition.
#[derive(Clone, Debug, PartialEq)]
pub struct Block {
    pub table: TableObjectData,
    pub record: BlockRecord,
    pub end: EndBlock,
    pub origin: Vector3,
    pub layer: String,
    pub description: String,
    pub flags: BlockTypeFlags,
    pub xref_file: String,
    pub is_xref_overlay: bool,
    pub entities: Vec<EntityObject>,
    pub attribute_definitions: IndexMap<String, AttributeDefinition>,
}

impl Block {
    pub const MODEL_SPACE_NAME: &'static str = "*Model_Space";
    pub const PAPER_SPACE_NAME: &'static str = "*Paper_Space";
    pub const DEFAULT_MODEL_SPACE_NAME: &'static str = "*Model_Space";
    pub const DEFAULT_PAPER_SPACE_NAME: &'static str = "*Paper_Space";

    pub fn create(
        name: impl Into<String>,
        entities: impl IntoIterator<Item = EntityObject>,
        attribute_definitions: impl IntoIterator<Item = AttributeDefinition>,
    ) -> Result<Self> {
        let mut blk = Self::new(name)?;
        for e in entities {
            blk.add_entity(e);
        }
        for a in attribute_definitions {
            blk.add_attribute_definition(a);
        }
        Ok(blk)
    }

    pub fn create_anonymous(name: impl Into<String>) -> Self {
        let raw = name.into();
        let anon_name = if raw.starts_with('*') {
            raw
        } else {
            format!("*{raw}")
        };
        let mut blk = Self::new_unchecked(anon_name, false);
        blk.flags |= BlockTypeFlags::ANONYMOUS_BLOCK;
        blk
    }

    #[inline]
    pub fn end_record(&self) -> &EndBlock {
        &self.end
    }

    pub fn new(name: impl Into<String>) -> Result<Self> {
        let table = TableObjectData::new(name, DxfObjectCode::BLOCK, true)?;
        let record = BlockRecord::new(table.name.clone());
        Ok(Self {
            table,
            record,
            end: EndBlock::new(),
            origin: Vector3::ZERO,
            layer: Layer::DEFAULT_NAME.to_string(),
            description: String::new(),
            flags: BlockTypeFlags::NONE,
            xref_file: String::new(),
            is_xref_overlay: false,
            entities: Vec::new(),
            attribute_definitions: IndexMap::new(),
        })
    }

    pub fn new_unchecked(name: impl Into<String>, reserved: bool) -> Self {
        let name_str = name.into();
        let table = if reserved {
            TableObjectData::reserved(name_str.clone(), DxfObjectCode::BLOCK)
        } else {
            TableObjectData::new(name_str.clone(), DxfObjectCode::BLOCK, false).unwrap()
        };
        Self {
            table,
            record: BlockRecord::new(name_str),
            end: EndBlock::new(),
            origin: Vector3::ZERO,
            layer: Layer::DEFAULT_NAME.to_string(),
            description: String::new(),
            flags: BlockTypeFlags::NONE,
            xref_file: String::new(),
            is_xref_overlay: false,
            entities: Vec::new(),
            attribute_definitions: IndexMap::new(),
        }
    }

    pub fn model_space() -> Self {
        Self::new_unchecked(Self::MODEL_SPACE_NAME, true)
    }

    pub fn paper_space() -> Self {
        Self::new_unchecked(Self::PAPER_SPACE_NAME, true)
    }

    pub fn add_entity(&mut self, entity: impl Into<EntityObject>) {
        self.entities.push(entity.into());
    }

    pub fn add_attribute_definition(&mut self, attdef: AttributeDefinition) {
        let key = attdef.tag.to_uppercase();
        self.attribute_definitions.insert(key, attdef);
        self.flags |= BlockTypeFlags::NON_CONSTANT_ATTRIBUTE_DEFINITIONS;
    }

    #[inline]
    pub fn is_for_internal_use_only(&self) -> bool {
        self.table.name.starts_with('*')
    }

    #[inline]
    pub fn is_xref(&self) -> bool {
        self.flags.contains(BlockTypeFlags::XREF)
    }

    pub fn rename_attribute_definitions(&mut self, renames: &[(String, String)]) {
        for (old_tag, new_tag) in renames {
            if let Some(mut attdef) = self.attribute_definitions.shift_remove(&old_tag.to_uppercase()) {
                attdef.tag = new_tag.clone();
                self.attribute_definitions.insert(new_tag.to_uppercase(), attdef);
            }
        }
    }

    pub fn prepare_attribute_definition_renames(&self, renames: &[(String, String)]) -> Vec<(String, String)> {
        renames.to_vec()
    }

    pub fn to_document(&self) -> crate::DxfDocument {
        let mut doc = crate::DxfDocument::new();
        for e in &self.entities {
            doc.entities.add(e.clone());
        }
        doc
    }

    pub fn load(path: impl AsRef<std::path::Path>) -> Result<Self> {
        let doc = crate::DxfDocument::load(path)?;
        let mut blk = Self::new("ImportedBlock")?;
        for e in doc.entities.iter() {
            blk.add_entity(e.clone());
        }
        Ok(blk)
    }

    pub fn save(&self, path: impl AsRef<std::path::Path>) -> Result<()> {
        let mut doc = self.to_document();
        doc.save(path)
    }
}

impl TableObject for Block {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
