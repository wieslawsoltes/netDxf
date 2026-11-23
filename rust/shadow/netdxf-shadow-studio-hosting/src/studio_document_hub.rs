#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioDocumentHubHttp;

impl StudioDocumentHubHttp {
    pub fn routes() -> &'static [&'static str] {
        &["/api/documents", "/api/documents/new", "/api/documents/close"]
    }
}

