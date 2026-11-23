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
                    ..Default::default()
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
                ..Default::default()
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
                    ..Default::default()
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
                ..Default::default()
            },
        ]
    }
}

