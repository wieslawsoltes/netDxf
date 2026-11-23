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

pub struct PipingModule;

impl PipingModule {
    pub fn create() -> DomainCadModule {
        DomainCadModule {
            descriptor: CadModuleDescriptor {
                id: "piping".into(),
                name: "Piping".into(),
                version: "1.0.0".into(),
                description: "Piping symbols, fittings, and line schedules".into(),
                commands: Vec::new(),
            },
            catalog: CadModuleCatalog::default(),
        }
    }
}

