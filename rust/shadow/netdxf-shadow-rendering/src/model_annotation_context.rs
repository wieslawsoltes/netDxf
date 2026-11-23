#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ModelAnnotationMode {
    #[default]
    Auto,
    ShowAll,
    MatchCurrentScale,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct AnnotationScale {
    pub paper_units: f64,
    pub drawing_units: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ModelAnnotationContext {
    pub mode: ModelAnnotationMode,
    pub current_scale: AnnotationScale,
    pub scale: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ModelAnnotationResolution {
    pub visible: bool,
    pub scale_factor: f64,
    pub enabled: bool,
    pub multiplier: f64,
    pub source: String,
    pub diagnostic: Option<String>,
}

impl ModelAnnotationContext {
    #[allow(clippy::should_implement_trait)]
    pub fn default() -> Self {
        Self {
            mode: ModelAnnotationMode::Auto,
            current_scale: AnnotationScale {
                paper_units: 1.0,
                drawing_units: 1.0,
            },
            scale: "1:1".to_string(),
        }
    }

    pub fn named_scales() -> Vec<(&'static str, AnnotationScale)> {
        vec![
            ("1:1", AnnotationScale { paper_units: 1.0, drawing_units: 1.0 }),
            ("1:2", AnnotationScale { paper_units: 1.0, drawing_units: 2.0 }),
            ("1:5", AnnotationScale { paper_units: 1.0, drawing_units: 5.0 }),
            ("1:10", AnnotationScale { paper_units: 1.0, drawing_units: 10.0 }),
            ("1:20", AnnotationScale { paper_units: 1.0, drawing_units: 20.0 }),
            ("1:50", AnnotationScale { paper_units: 1.0, drawing_units: 50.0 }),
            ("1:100", AnnotationScale { paper_units: 1.0, drawing_units: 100.0 }),
        ]
    }

    pub fn resolve(&self) -> ModelAnnotationResolution {
        let factor = if self.current_scale.paper_units.abs() > 1e-12 {
            self.current_scale.drawing_units / self.current_scale.paper_units
        } else {
            1.0
        };
        ModelAnnotationResolution {
            visible: true,
            scale_factor: factor,
            enabled: self.mode != ModelAnnotationMode::Auto,
            multiplier: factor,
            source: self.scale.clone(),
            diagnostic: None,
        }
    }
}

