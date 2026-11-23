//! Engineering symbol catalog, domain connectivity, isolation, and schedules (`netDxf.Shadow.Symbols`).

#![forbid(unsafe_code)]

use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
use serde::{Deserialize, Serialize};
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SymbolAttribute {
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingBlockEntry {
    pub name: String,
    pub entity_count: usize,
}

pub struct DrawingBlockLibrary;
pub struct SymbolAuthoring;
pub struct SymbolCatalog;
pub struct SymbolNative;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainIssue {
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub entity: Option<ObjectId>,
    pub message: String,
}

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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainScan {
    pub entities: Vec<DomainEntity>,
    pub issues: Vec<DomainIssue>,
}

pub struct DomainAnalysis;
pub struct DomainMetadata;
pub struct DomainPlans;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DomainConnectivityOptions {
    pub tolerance: f64,
    pub maximum_nodes: usize,
}

impl Default for DomainConnectivityOptions {
    fn default() -> Self {
        Self {
            tolerance: 0.05,
            maximum_nodes: 50_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainPortNode {
    pub id: usize,
    pub entity: ObjectId,
    pub port_name: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainConnection {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub distance: f64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainConnectivityIssue {
    pub code: String,
    pub message: String,
    pub entity: Option<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainNetworkComponent {
    pub id: usize,
    pub nodes: Vec<usize>,
}

pub struct DomainConnectivity;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationOptions {
    pub respect_closed_valves: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainIsolationExclusion {
    pub entity: ObjectId,
    pub reason: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationTraversalSummary {
    pub visited_nodes: usize,
    pub blocked_nodes: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationAssessment {
    pub isolated: bool,
    pub exclusions: Vec<DomainIsolationExclusion>,
    pub summary: DomainIsolationTraversalSummary,
}

pub struct DomainConnectivityIsolation;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainStructureOptions {
    pub maximum_loops: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainStrongComponent {
    pub id: usize,
    pub nodes: Vec<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainPortStructure {
    pub node: usize,
    pub component: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainCondensationLink {
    pub source_component: usize,
    pub target_component: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainCriticalConnection {
    pub connection_id: usize,
    pub is_bridge: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainLoopStep {
    pub node: usize,
    pub connection: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainLoop {
    pub steps: Vec<DomainLoopStep>,
}

pub struct DomainConnectivityStructure;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainPortVisit {
    pub node: usize,
    pub depth: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainConnectionProof {
    pub connected: bool,
    pub path: Vec<DomainPortVisit>,
}

pub struct DomainConnectivityWalk;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainScheduleOptions {
    pub group_by: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainScheduleRow {
    pub entity: ObjectId,
    pub tag: String,
    pub columns: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainScheduleGroup {
    pub key: String,
    pub rows: Vec<DomainScheduleRow>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainScheduleIssue {
    pub code: String,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainSchedule {
    pub groups: Vec<DomainScheduleGroup>,
    pub issues: Vec<DomainScheduleIssue>,
}
