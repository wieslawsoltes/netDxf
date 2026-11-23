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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SymbolStroke {
    pub kind: SymbolStrokeKind,
    pub points: Vec<Vec3>,
    pub start: Vec3,
    pub end: Vec3,
    pub start_angle: f64,
    pub end_angle: f64,
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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SymbolPort {
    pub id: String,
    pub name: String,
    pub kind: String,
    pub nominal_size: Option<String>,
    pub position: Vec3,
    pub direction: Vec3,
    pub medium: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SymbolDefinition {
    pub id: String,
    pub name: String,
    pub domain: String,
    pub category: String,
    pub description: String,
    pub version: String,
    pub units: String,
    pub base_point: Vec3,
    pub standards: Vec<String>,
    pub strokes: Vec<SymbolStroke>,
    pub geometry: Vec<SymbolStroke>,
    pub ports: Vec<SymbolPort>,
    pub passages: Vec<DomainPassage>,
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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainReference {
    pub kind: String,
    pub target: ObjectId,
    pub port_id: Option<String>,
    pub role: Option<String>,
    pub target_key: Option<String>,
    pub target_port_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainRecord {
    pub id: ObjectId,
    pub key: String,
    pub domain: String,
    pub module: String,
    pub class: String,
    pub symbol_id: String,
    pub symbol_version: String,
    pub schema_version: u32,
    pub tag: String,
    pub properties: BTreeMap<String, String>,
    pub ports: Vec<SymbolPort>,
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

