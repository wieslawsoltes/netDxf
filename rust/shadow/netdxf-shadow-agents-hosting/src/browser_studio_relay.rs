#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct BrowserStudioRelay;

impl BrowserStudioRelay {
    pub fn route_prefix() -> &'static str {
        "/api/studio/relay"
    }
}

