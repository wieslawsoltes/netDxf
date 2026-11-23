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
    disabled: std::collections::BTreeSet<String>,
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

    pub fn is_enabled(&self, id: &str) -> bool {
        self.modules.contains_key(id) && !self.disabled.contains(id)
    }

    pub fn set_enabled(&mut self, id: &str, enabled: bool) -> bool {
        if !self.modules.contains_key(id) {
            return false;
        }
        if enabled {
            self.disabled.remove(id);
        } else {
            self.disabled.insert(id.to_string());
        }
        true
    }

    pub fn descriptors(&self) -> Vec<CadModuleDescriptor> {
        self.modules.values().map(|m| m.descriptor()).collect()
    }

    pub fn catalog(&self, id: &str) -> Option<CadModuleCatalog> {
        self.modules.get(id).map(|m| m.catalog())
    }

    pub fn analyze(
        &self,
        id: &str,
        snapshot: &ShadowSnapshot,
        options: &CadModuleAnalysisOptions,
    ) -> Option<CadModuleAnalysis> {
        self.modules.get(id).map(|m| m.analyze(snapshot, options))
    }

    pub fn plan(&self, id: &str, command: &CadModuleCommandRequest) -> Result<Value, String> {
        if !self.is_enabled(id) {
            return Err(format!("Module '{id}' is not enabled."));
        }
        Ok(serde_json::json!({
            "moduleId": id,
            "command": command.command,
            "arguments": command.arguments,
        }))
    }

    pub fn states(&self) -> Vec<(String, bool)> {
        self.modules
            .keys()
            .map(|id| (id.clone(), !self.disabled.contains(id)))
            .collect()
    }

    pub fn state(&self) -> CadModuleState {
        CadModuleState {
            registered_modules: self.modules.keys().cloned().collect(),
        }
    }
}

