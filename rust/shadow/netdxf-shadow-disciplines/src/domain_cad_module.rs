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

pub struct DomainValidation;

impl DomainValidation {
    pub fn validate_non_empty_tag(tag: &str) -> Result<(), String> {
        if tag.trim().is_empty() {
            Err("Domain tag cannot be empty.".into())
        } else {
            Ok(())
        }
    }
}

