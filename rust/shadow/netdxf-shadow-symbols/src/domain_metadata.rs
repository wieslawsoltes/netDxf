#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct DomainMetadata;

impl DomainMetadata {
    pub fn property<'a>(record: &'a DomainRecord, key: &str) -> Option<&'a str> {
        record.properties.get(key).map(String::as_str)
    }
}

