#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::ShadowSnapshot;
#[allow(unused_imports)]
use netdxf_shadow_modules::{CadModuleAnalysis, CadModuleAnalysisOptions, CadModuleCatalog, CadModuleDescriptor, ICadModule};

#[derive(Debug, Clone)]
pub struct DomainCadModule {
    pub descriptor: CadModuleDescriptor,
    pub catalog: CadModuleCatalog,
}

impl ICadModule for DomainCadModule {
    fn descriptor(&self) -> CadModuleDescriptor {
        self.descriptor.clone()
    }

    fn catalog(&self) -> CadModuleCatalog {
        self.catalog.clone()
    }

    fn analyze(
        &self,
        _snapshot: &ShadowSnapshot,
        _options: &CadModuleAnalysisOptions,
    ) -> CadModuleAnalysis {
        CadModuleAnalysis {
            module_id: self.descriptor.id.clone(),
            ..Default::default()
        }
    }
}

impl DomainCadModule {
    pub fn plan(&self, _snapshot: &ShadowSnapshot) -> Option<String> {
        None
    }
}

#[derive(Debug, Clone, Default)]
pub struct DomainValidation {
    pub issues: Vec<String>,
    pub records: usize,
    pub truncated: bool,
}

impl DomainValidation {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn add(&mut self, issue: impl Into<String>) {
        self.issues.push(issue.into());
    }

    pub fn require(&mut self, condition: bool, message: impl Into<String>) -> bool {
        if !condition {
            self.issues.push(message.into());
        }
        condition
    }

    pub fn positive(&mut self, value: f64, name: &str) -> bool {
        if value.is_finite() && value > 0.0 {
            true
        } else {
            self.issues
                .push(format!("{name} must be positive and finite."));
            false
        }
    }

    pub fn value(&self) -> bool {
        self.issues.is_empty()
    }

    pub fn consistent_routes(&mut self, routes: &[(&str, &str)]) -> bool {
        let mut ok = true;
        for (from, to) in routes {
            if from.trim().is_empty() || to.trim().is_empty() || from == to {
                self.issues
                    .push(format!("Inconsistent route '{from}' -> '{to}'."));
                ok = false;
            }
        }
        ok
    }

    pub fn validate_non_empty_tag(tag: &str) -> Result<(), String> {
        if tag.trim().is_empty() {
            Err("Domain tag cannot be empty.".into())
        } else {
            Ok(())
        }
    }
}

