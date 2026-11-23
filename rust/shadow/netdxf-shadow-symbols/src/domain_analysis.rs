#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainIssue {
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub entity: Option<ObjectId>,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainScan {
    pub entities: Vec<DomainEntity>,
    pub issues: Vec<DomainIssue>,
}

pub struct DomainAnalysis;

impl DomainAnalysis {
    pub fn scan(entities: Vec<DomainEntity>) -> DomainScan {
        let mut issues = Vec::new();
        for e in &entities {
            if e.record.tag.trim().is_empty() {
                issues.push(DomainIssue {
                    code: "MISSING_TAG".into(),
                    severity: DiagnosticSeverity::Warning,
                    entity: Some(e.entity.clone()),
                    message: format!("Domain entity '{}' has an empty tag.", e.record.symbol_id),
                });
            }
        }
        DomainScan { entities, issues }
    }
}

