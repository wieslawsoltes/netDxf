#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum SymbolStrokeKind {
    #[default]
    Line,
    Circle,
    Arc,
    Polyline,
    Text,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SymbolStroke {
    pub kind: SymbolStrokeKind,
    pub points: Vec<Vec3>,
    pub radius: f64,
    pub text: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SymbolAttribute {
    pub id: String,
    pub name: String,
    pub tag: String,
    pub prompt: String,
    pub default: String,
    pub default_value: String,
    pub position: Vec3,
    pub height: f64,
    pub required: bool,
    pub visible: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SymbolPort {
    pub name: String,
    pub position: Vec3,
    pub direction: Vec3,
    pub medium: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SymbolDefinition {
    pub id: String,
    pub name: String,
    pub domain: String,
    pub category: String,
    pub strokes: Vec<SymbolStroke>,
    pub ports: Vec<SymbolPort>,
    pub attributes: Vec<SymbolAttribute>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SymbolCatalogPage {
    pub total: usize,
    pub offset: usize,
    pub items: Vec<SymbolDefinition>,
}

pub struct SymbolCatalog;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainPassage {
    pub from_port: String,
    pub to_port: String,
    pub open: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainReference {
    pub kind: String,
    pub target: ObjectId,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainRecord {
    pub id: ObjectId,
    pub domain: String,
    pub symbol_id: String,
    pub tag: String,
    pub properties: BTreeMap<String, String>,
    pub passages: Vec<DomainPassage>,
    pub references: Vec<DomainReference>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainEntity {
    pub entity: ObjectId,
    pub record: DomainRecord,
    pub position: Vec3,
}

impl SymbolCatalog {
    pub fn paginate(items: &[SymbolDefinition], offset: usize, limit: usize) -> SymbolCatalogPage {
        SymbolCatalogPage {
            total: items.len(),
            offset,
            items: items.iter().skip(offset).take(limit).cloned().collect(),
        }
    }
}

