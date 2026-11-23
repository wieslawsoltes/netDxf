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

pub trait ITextOutlineProvider: Send + Sync {
    fn text_outlines(&self, text: &str, font: &str, height: f64) -> Vec<RenderContour>;

    fn outline(&self, text: &str, font: &str, height: f64) -> Vec<RenderContour> {
        self.text_outlines(text, font, height)
    }
}

