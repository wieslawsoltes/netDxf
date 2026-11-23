#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct CompanionWorkspace;

impl CompanionWorkspace {
    pub const PREFIX: &'static str = "companion.";

    pub fn default_dir() -> &'static str {
        ".shadow-companion"
    }

    pub fn tools() -> &'static [&'static str] {
        &["companion.read", "companion.write", "companion.list"]
    }

    pub fn execute(tool: &str, args: &Value) -> Value {
        serde_json::json!({ "tool": tool, "args": args, "ok": true })
    }
}

