#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct CadModuleField {
    pub id: String,
    pub name: String,
    pub field_type: String,
    pub r#type: String,
    pub required: bool,
    pub description: String,
    pub default: Option<String>,
    pub choices: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct CadModuleCommand {
    pub id: String,
    pub name: String,
    pub description: String,
    pub requires_selection: bool,
    pub requires_symbol: bool,
    pub minimum_points: usize,
    pub maximum_points: usize,
    pub fields: Vec<CadModuleField>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct CadModuleDescriptor {
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: String,
    pub commands: Vec<CadModuleCommand>,
}

impl CadModuleDescriptor {
    pub fn schema_version(&self) -> u32 {
        1
    }

    pub fn standards(&self) -> &[String] {
        &[]
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModulePort {
    pub id: String,
    pub name: String,
    pub kind: String,
    pub direction: String,
    pub nominal_size: Option<String>,
    pub offset: Vec3,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleSymbol {
    pub id: String,
    pub symbol_id: String,
    pub name: String,
    pub description: String,
    pub version: String,
    pub units: String,
    pub category: String,
    pub base_point: Vec3,
    pub standards: Vec<String>,
    pub ports: Vec<CadModulePort>,
    pub attributes: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleCatalog {
    pub symbols: Vec<CadModuleSymbol>,
    pub items: Vec<CadModuleSymbol>,
    pub limit: usize,
    pub offset: usize,
    pub total: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleRecord {
    pub entity: ObjectId,
    pub module_id: String,
    pub symbol_id: String,
    pub class: String,
    pub tag: String,
    pub ports: Vec<CadModulePort>,
    pub properties: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CadModuleIssue {
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub entity: Option<ObjectId>,
    pub message: String,
}

impl CadModuleIssue {
    pub fn entities(&self) -> Vec<ObjectId> {
        self.entity.iter().cloned().collect()
    }

    pub fn position(&self) -> Option<Vec3> {
        None
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleAnalysis {
    pub document_id: String,
    pub revision: i64,
    pub module_id: String,
    pub complete: bool,
    pub objects_visited: usize,
    pub limitation: Option<String>,
    pub records: Vec<CadModuleRecord>,
    pub issues: Vec<CadModuleIssue>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleAnalysisOptions {
    pub include_connectivity: bool,
    pub connection_tolerance: f64,
    pub entities: Vec<ObjectId>,
    pub maximum_issues: usize,
    pub maximum_objects: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleCommandRequest {
    pub module_id: String,
    pub command: String,
    pub symbol_id: Option<String>,
    pub symbol_version: Option<String>,
    pub entities: Vec<ObjectId>,
    pub points: Vec<Vec3>,
    pub properties: BTreeMap<String, String>,
    pub arguments: Value,
}

pub trait ICadModule: Send + Sync {
    fn descriptor(&self) -> CadModuleDescriptor;
    fn catalog(&self) -> CadModuleCatalog;
    fn analyze(
        &self,
        snapshot: &ShadowSnapshot,
        options: &CadModuleAnalysisOptions,
    ) -> CadModuleAnalysis;

    fn plan(&self, _snapshot: &ShadowSnapshot, _request: &CadModuleCommandRequest) -> Option<Value> {
        None
    }
}

