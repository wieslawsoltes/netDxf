//! Piping engineering CAD discipline module (`netDxf.Shadow.Piping`).

#![forbid(unsafe_code)]

use netdxf_shadow_disciplines::DomainCadModule;
use netdxf_shadow_modules::{CadModuleCatalog, CadModuleDescriptor};
use netdxf_shadow_symbols::SymbolDefinition;

pub struct PipingSymbols;

impl PipingSymbols {
    pub fn catalog() -> Vec<SymbolDefinition> {
        Vec::new()
    }
}

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
