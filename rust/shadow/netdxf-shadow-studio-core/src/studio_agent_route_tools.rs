#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadScene, CadSceneBuilder, SceneOptions};
#[allow(unused_imports)]
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
#[allow(unused_imports)]
use netdxf_shadow_rendering::{RenderCompileOptions, RenderCompiler, RenderScene};
#[allow(unused_imports)]
use netdxf_shadow_samples::StudioSamples;
#[allow(unused_imports)]
use netdxf_shadow_workflows::IWorkflowDriver;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use std::sync::Arc;

pub struct StudioAgentRouteTools;

impl StudioAgentRouteTools {
    pub fn routes() -> Vec<Route> {
        vec![
            Route {
                method: "GET".into(),
                path: "/api/cad/snapshot".into(),
                description: "Read current shadow snapshot summary".into(),
            },
            Route {
                method: "POST".into(),
                path: "/api/cad/pick".into(),
                description: "Hit-test entities near a world point".into(),
            },
        ]
    }
}

