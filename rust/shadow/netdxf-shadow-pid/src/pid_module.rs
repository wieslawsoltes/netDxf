#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::Vec3;
#[allow(unused_imports)]
use netdxf_shadow_disciplines::DomainCadModule;
#[allow(unused_imports)]
use netdxf_shadow_modules::{CadModuleCatalog, CadModuleDescriptor};
#[allow(unused_imports)]
use netdxf_shadow_symbols::{SymbolDefinition, SymbolPort, SymbolStroke, SymbolStrokeKind};

pub struct PidModule;

impl PidModule {
    pub fn descriptor() -> CadModuleDescriptor {
        Self::create().descriptor
    }

    pub fn create() -> DomainCadModule {
        DomainCadModule {
            descriptor: CadModuleDescriptor {
                id: "pid".into(),
                name: "P&ID".into(),
                version: "1.0.0".into(),
                description: "Process and instrumentation diagram symbols and flow connectivity"
                    .into(),
                commands: Vec::new(),
            },
            catalog: CadModuleCatalog::default(),
        }
    }
}

