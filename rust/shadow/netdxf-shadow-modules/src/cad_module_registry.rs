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
pub struct CadModuleState {
    pub registered_modules: Vec<String>,
}

#[derive(Default)]
pub struct CadModuleRegistry {
    modules: BTreeMap<String, Box<dyn ICadModule>>,
}

impl CadModuleRegistry {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn register(&mut self, module: Box<dyn ICadModule>) {
        let id = module.descriptor().id;
        self.modules.insert(id, module);
    }

    pub fn get(&self, id: &str) -> Option<&dyn ICadModule> {
        self.modules.get(id).map(|b| b.as_ref())
    }

    pub fn descriptors(&self) -> Vec<CadModuleDescriptor> {
        self.modules.values().map(|m| m.descriptor()).collect()
    }

    pub fn state(&self) -> CadModuleState {
        CadModuleState {
            registered_modules: self.modules.keys().cloned().collect(),
        }
    }
}

