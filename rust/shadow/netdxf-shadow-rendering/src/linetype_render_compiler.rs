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

pub trait ILinetypeDecorationProvider: Send + Sync {
    fn resolve_decoration(&self, name: &str) -> Option<Vec<RenderContour>>;

    fn revision(&self) -> i64 {
        0
    }

    fn try_project(&self, name: &str) -> Option<Vec<RenderContour>> {
        self.resolve_decoration(name)
    }
}

