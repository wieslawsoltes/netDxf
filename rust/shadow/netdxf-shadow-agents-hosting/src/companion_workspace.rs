#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct CompanionWorkspace;

impl CompanionWorkspace {
    pub fn default_dir() -> &'static str {
        ".shadow-companion"
    }
}

