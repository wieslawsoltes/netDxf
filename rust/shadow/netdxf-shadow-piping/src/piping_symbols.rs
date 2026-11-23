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

pub struct PipingSymbols;

impl PipingSymbols {
    pub fn catalog() -> Vec<SymbolDefinition> {
        vec![
            SymbolDefinition {
                id: "piping.elbow90".into(),
                name: "90-Degree Elbow".into(),
                domain: "piping".into(),
                category: "Fittings".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Arc,
                    points: vec![Vec3::xy(-3.0, 0.0), Vec3::xy(0.0, 3.0)],
                    radius: 3.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "P1".into(),
                        position: Vec3::xy(-3.0, 0.0),
                        direction: Vec3::xy(-1.0, 0.0),
                        medium: "pipe".into(),
                    },
                    SymbolPort {
                        name: "P2".into(),
                        position: Vec3::xy(0.0, 3.0),
                        direction: Vec3::xy(0.0, 1.0),
                        medium: "pipe".into(),
                    },
                ],
                attributes: Vec::new(),
                ..Default::default()
            },
            SymbolDefinition {
                id: "piping.flange".into(),
                name: "Weld Neck Flange".into(),
                domain: "piping".into(),
                category: "Connections".into(),
                strokes: vec![SymbolStroke {
                    kind: SymbolStrokeKind::Line,
                    points: vec![Vec3::xy(0.0, -3.0), Vec3::xy(0.0, 3.0)],
                    radius: 0.0,
                    text: None,
                }],
                ports: vec![
                    SymbolPort {
                        name: "FACE".into(),
                        position: Vec3::ZERO,
                        direction: Vec3::xy(1.0, 0.0),
                        medium: "pipe".into(),
                    },
                ],
                attributes: Vec::new(),
                ..Default::default()
            },
        ]
    }
}

