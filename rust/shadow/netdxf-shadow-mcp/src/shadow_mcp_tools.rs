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

pub struct ShadowMcpTools;

impl ShadowMcpTools {
    pub fn standard_tool_names() -> &'static [&'static str] {
        &[
            "shadow.document.summary",
            "shadow.entities.list",
            "shadow.references.capture",
            "shadow.references.validate",
        ]
    }
}

