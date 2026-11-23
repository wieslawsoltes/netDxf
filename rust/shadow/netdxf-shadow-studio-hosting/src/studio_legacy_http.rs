#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioLegacyHttp;

impl StudioLegacyHttp {
    pub fn is_deprecated() -> bool {
        true
    }
}

