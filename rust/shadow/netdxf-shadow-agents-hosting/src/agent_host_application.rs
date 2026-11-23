#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct AgentHostApplication {
    pub options: AgentHostOptions,
}

impl AgentHostApplication {
    pub fn build(options: AgentHostOptions) -> Self {
        Self { options }
    }
}

