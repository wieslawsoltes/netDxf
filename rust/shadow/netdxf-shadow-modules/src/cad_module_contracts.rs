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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CadModuleField {
    pub name: String,
    pub field_type: String,
    pub required: bool,
    pub description: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CadModuleCommand {
    pub name: String,
    pub description: String,
    pub fields: Vec<CadModuleField>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CadModuleDescriptor {
    pub id: String,
    pub name: String,
    pub version: String,
    pub description: String,
    pub commands: Vec<CadModuleCommand>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadModulePort {
    pub name: String,
    pub direction: String,
    pub offset: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadModuleSymbol {
    pub id: String,
    pub name: String,
    pub category: String,
    pub ports: Vec<CadModulePort>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleCatalog {
    pub symbols: Vec<CadModuleSymbol>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadModuleRecord {
    pub entity: ObjectId,
    pub module_id: String,
    pub symbol_id: String,
    pub properties: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CadModuleIssue {
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub entity: Option<ObjectId>,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleAnalysis {
    pub module_id: String,
    pub records: Vec<CadModuleRecord>,
    pub issues: Vec<CadModuleIssue>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct CadModuleAnalysisOptions {
    pub include_connectivity: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadModuleCommandRequest {
    pub module_id: String,
    pub command: String,
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
}

