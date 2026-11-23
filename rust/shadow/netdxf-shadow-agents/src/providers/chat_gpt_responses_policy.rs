#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use thiserror::Error;

pub struct ChatGptResponsesContract;

impl ChatGptResponsesContract {
    pub const ENDPOINT: &'static str = "https://api.openai.com/v1/responses";
}

