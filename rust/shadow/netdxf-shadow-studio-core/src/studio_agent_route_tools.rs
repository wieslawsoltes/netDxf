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
    pub fn is_studio_path(path: &str) -> bool {
        path.starts_with("/api/cad") || path.starts_with("/api/studio")
    }

    pub fn register_read() -> Vec<Route> {
        Self::routes().into_iter().filter(|r| !r.mutates).collect()
    }

    pub fn routes() -> Vec<Route> {
        vec![
            Route {
                name: "studio.cad.snapshot".into(),
                method: "GET".into(),
                path: "/api/cad/snapshot".into(),
                description: "Read current shadow snapshot summary".into(),
                mutates: false,
                schema: Value::Null,
            },
            Route {
                name: "studio.cad.pick".into(),
                method: "POST".into(),
                path: "/api/cad/pick".into(),
                description: "Hit-test entities near a world point".into(),
                mutates: true,
                schema: Value::Null,
            },
        ]
    }
}

