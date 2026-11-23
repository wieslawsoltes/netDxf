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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ViewportLayerOverride {
    pub layer: String,
    pub frozen: Option<bool>,
    pub argb: Option<u32>,
    pub lineweight: Option<f64>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ViewportLayerProfile {
    pub overrides: BTreeMap<String, ViewportLayerOverride>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ViewportOverrideLimits {
    pub maximum_viewports: usize,
    pub maximum_overrides_per_viewport: usize,
}

impl Default for ViewportOverrideLimits {
    fn default() -> Self {
        Self {
            maximum_viewports: 1_024,
            maximum_overrides_per_viewport: 8_192,
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct ViewportOverrideCatalog {
    pub profiles: BTreeMap<ObjectId, ViewportLayerProfile>,
}

