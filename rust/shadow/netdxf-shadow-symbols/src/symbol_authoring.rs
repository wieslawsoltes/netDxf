#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct SymbolAuthoring;

impl SymbolAuthoring {
    pub fn attach(entity: ObjectId, record: DomainRecord, position: Vec3) -> DomainEntity {
        DomainEntity {
            entity,
            record,
            position,
        }
    }

    pub fn place(symbol: &SymbolDefinition, position: Vec3) -> DomainEntity {
        DomainEntity {
            entity: ObjectId::new(format!("SYM:{}", symbol.id)),
            record: DomainRecord {
                symbol_id: symbol.id.clone(),
                domain: symbol.domain.clone(),
                ..Default::default()
            },
            position,
        }
    }

    pub fn create_simple(
        id: impl Into<String>,
        name: impl Into<String>,
        domain: impl Into<String>,
    ) -> SymbolDefinition {
        SymbolDefinition {
            id: id.into(),
            name: name.into(),
            domain: domain.into(),
            category: "Custom".into(),
            strokes: Vec::new(),
            ports: Vec::new(),
            attributes: Vec::new(),
            ..Default::default()
        }
    }
}

