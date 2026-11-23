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

pub struct ShadowMcpServer {
    pub identity: ShadowMcpIdentity,
    pub registry: McpToolRegistry,
}

impl ShadowMcpServer {
    pub fn new(identity: ShadowMcpIdentity) -> Self {
        Self {
            identity,
            registry: McpToolRegistry::new(),
        }
    }
}

