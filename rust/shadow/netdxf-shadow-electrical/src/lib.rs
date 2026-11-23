//! Electrical engineering CAD discipline module (`netDxf.Shadow.Electrical`).

#![forbid(unsafe_code)]

use netdxf_shadow_disciplines::DomainCadModule;
use netdxf_shadow_modules::{CadModuleCatalog, CadModuleDescriptor};
use netdxf_shadow_symbols::SymbolDefinition;

pub struct ElectricalSymbols;

impl ElectricalSymbols {
    pub fn catalog() -> Vec<SymbolDefinition> {
        Vec::new()
    }
}

pub struct ElectricalModule;

impl ElectricalModule {
    pub fn create() -> DomainCadModule {
        DomainCadModule {
            descriptor: CadModuleDescriptor {
                id: "electrical".into(),
                name: "Electrical".into(),
                version: "1.0.0".into(),
                description: "Electrical schematic symbols and circuit connectivity".into(),
                commands: Vec::new(),
            },
            catalog: CadModuleCatalog::default(),
        }
    }
}
