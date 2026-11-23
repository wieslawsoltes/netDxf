//! Electrical engineering CAD discipline module (`netDxf.Shadow.Electrical`).

#![forbid(unsafe_code)]

use netdxf_shadow::Vec3;
use netdxf_shadow_disciplines::DomainCadModule;
use netdxf_shadow_modules::{CadModuleCatalog, CadModuleDescriptor};
use netdxf_shadow_symbols::{SymbolDefinition, SymbolPort, SymbolStroke, SymbolStrokeKind};

pub struct ElectricalSymbols;

impl ElectricalSymbols {
    pub fn catalog() -> Vec<SymbolDefinition> {
        vec![
            SymbolDefinition {
                id: "elec.resistor".into(),
                name: "Resistor".into(),
                domain: "electrical".into(),
                category: "Passive".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Line,
                    points: vec![Vec3::xy(-5.0, 0.0), Vec3::xy(5.0, 0.0)],
                    radius: 0.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "IN".into(),
                        position: Vec3::xy(-5.0, 0.0),
                        direction: Vec3::xy(-1.0, 0.0),
                        medium: "electrical".into(),
                    },
                    SymbolPort {
                        name: "OUT".into(),
                        position: Vec3::xy(5.0, 0.0),
                        direction: Vec3::xy(1.0, 0.0),
                        medium: "electrical".into(),
                    },
                ],
                attributes: Vec::new(),
            },
            SymbolDefinition {
                id: "elec.breaker".into(),
                name: "Circuit Breaker".into(),
                domain: "electrical".into(),
                category: "Protection".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Line,
                    points: vec![Vec3::xy(-4.0, 0.0), Vec3::xy(4.0, 2.0)],
                    radius: 0.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "LINE".into(),
                        position: Vec3::xy(-4.0, 0.0),
                        direction: Vec3::xy(-1.0, 0.0),
                        medium: "electrical".into(),
                    },
                    SymbolPort {
                        name: "LOAD".into(),
                        position: Vec3::xy(4.0, 0.0),
                        direction: Vec3::xy(1.0, 0.0),
                        medium: "electrical".into(),
                    },
                ],
                attributes: Vec::new(),
            },
        ]
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
