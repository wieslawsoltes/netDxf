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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Route {
    pub name: String,
    pub method: String,
    pub path: String,
    pub description: String,
    pub mutates: bool,
    pub schema: Value,
}

pub struct StudioAgentSchema;

impl StudioAgentSchema {
    pub fn routes() -> Vec<Route> {
        StudioAgentRouteTools::routes()
    }

    pub fn find(name: &str) -> Option<Route> {
        Self::routes().into_iter().find(|r| r.name == name || r.path == name)
    }

    pub fn parse_object(input: &str) -> Result<Value, String> {
        serde_json::from_str(input).map_err(|e| e.to_string())
    }

    pub fn tool_schema() -> Value {
        Self::route_schema()
    }

    pub fn route_schema() -> Value {
        serde_json::json!({
            "type": "object",
            "routes": StudioAgentRouteTools::routes(),
        })
    }
}

