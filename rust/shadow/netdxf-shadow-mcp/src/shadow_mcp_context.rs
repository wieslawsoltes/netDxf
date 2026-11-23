#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use std::sync::Arc;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowMcpIdentity {
    pub name: String,
    pub version: String,
}

impl Default for ShadowMcpIdentity {
    fn default() -> Self {
        Self {
            name: "netdxf-shadow-mcp".into(),
            version: "0.1.0".into(),
        }
    }
}

pub struct ShadowMcpContext {
    pub snapshot: Arc<ShadowSnapshot>,
}

