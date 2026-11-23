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

/// Workflow replay driver executing operations against a [`Studio`] instance.
pub struct StudioWorkflowDriver {
    pub studio: Studio,
}

impl IWorkflowDriver for StudioWorkflowDriver {
    fn execute(&mut self, operation: &str, _arguments: &Value) -> Result<Value, String> {
        match operation {
            "snapshot.revision" => Ok(serde_json::json!({
                "revision": self.studio.current_snapshot().revision
            })),
            _ => Ok(serde_json::json!({ "ok": true, "operation": operation })),
        }
    }
}

impl StudioWorkflowDriver {
    pub fn initialize_async(&mut self) -> Result<(), String> {
        Ok(())
    }
}

