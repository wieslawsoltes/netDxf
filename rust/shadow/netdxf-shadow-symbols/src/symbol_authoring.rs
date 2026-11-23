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

