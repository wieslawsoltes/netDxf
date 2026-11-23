//! P&ID engineering CAD discipline module (`netDxf.Shadow.Pid`).

#![forbid(unsafe_code)]

use netdxf_shadow::Vec3;
use netdxf_shadow_disciplines::DomainCadModule;
use netdxf_shadow_modules::{CadModuleCatalog, CadModuleDescriptor};
use netdxf_shadow_symbols::{SymbolDefinition, SymbolPort, SymbolStroke, SymbolStrokeKind};

pub struct PidSymbols;

impl PidSymbols {
    pub fn catalog() -> Vec<SymbolDefinition> {
        vec![
            SymbolDefinition {
                id: "pid.pump".into(),
                name: "Centrifugal Pump".into(),
                domain: "pid".into(),
                category: "Rotating Equipment".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Circle,
                    points: vec![Vec3::ZERO],
                    radius: 5.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "SUCTION".into(),
                        position: Vec3::xy(-5.0, 0.0),
                        direction: Vec3::xy(-1.0, 0.0),
                        medium: "process".into(),
                    },
                    SymbolPort {
                        name: "DISCHARGE".into(),
                        position: Vec3::xy(5.0, 0.0),
                        direction: Vec3::xy(1.0, 0.0),
                        medium: "process".into(),
                    },
                ],
                attributes: Vec::new(),
            },
            SymbolDefinition {
                id: "pid.valve.gate".into(),
                name: "Gate Valve".into(),
                domain: "pid".into(),
                category: "Valves".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Polyline,
                    points: vec![
                        Vec3::xy(-4.0, -2.5),
                        Vec3::xy(4.0, 2.5),
                        Vec3::xy(4.0, -2.5),
                        Vec3::xy(-4.0, 2.5),
                    ],
                    radius: 0.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "IN".into(),
                        position: Vec3::xy(-4.0, 0.0),
                        direction: Vec3::xy(-1.0, 0.0),
                        medium: "process".into(),
                    },
                    SymbolPort {
                        name: "OUT".into(),
                        position: Vec3::xy(4.0, 0.0),
                        direction: Vec3::xy(1.0, 0.0),
                        medium: "process".into(),
                    },
                ],
                attributes: Vec::new(),
            },
        ]
    }
}

pub struct PidModule;

impl PidModule {
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
