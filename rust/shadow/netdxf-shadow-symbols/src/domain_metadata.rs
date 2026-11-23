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
    pub const APPLICATION_NAME: &'static str = "NETDXF_SHADOW_DOMAIN";
    pub const MAXIMUM_BYTES: usize = 65_536;

    pub fn application_name() -> &'static str {
        Self::APPLICATION_NAME
    }

    pub fn maximum_bytes() -> usize {
        Self::MAXIMUM_BYTES
    }

    pub fn property<'a>(record: &'a DomainRecord, key: &str) -> Option<&'a str> {
        record.properties.get(key).map(String::as_str)
    }

    pub fn chunks(payload: &str, max_chunk: usize) -> Vec<String> {
        let step = max_chunk.max(1);
        let bytes = payload.as_bytes();
        let mut out = Vec::new();
        let mut idx = 0;
        while idx < bytes.len() {
            let mut end = (idx + step).min(bytes.len());
            while end > idx && !payload.is_char_boundary(end) {
                end -= 1;
            }
            out.push(payload[idx..end].to_string());
            idx = end;
        }
        out
    }

    pub fn write(record: &DomainRecord) -> Result<String, String> {
        let json = serde_json::to_string(record).map_err(|e| e.to_string())?;
        if json.len() > Self::MAXIMUM_BYTES {
            return Err("Domain metadata exceeds maximum byte budget.".into());
        }
        Ok(json)
    }

    pub fn read(payload: &str) -> Result<DomainRecord, String> {
        if payload.len() > Self::MAXIMUM_BYTES {
            return Err("Domain metadata exceeds maximum byte budget.".into());
        }
        serde_json::from_str(payload).map_err(|e| e.to_string())
    }

    pub fn validate(record: &DomainRecord) -> Vec<String> {
        let mut issues = Vec::new();
        if record.domain.trim().is_empty() {
            issues.push("Domain record domain must not be empty.".into());
        }
        if record.symbol_id.trim().is_empty() {
            issues.push("Domain record symbol_id must not be empty.".into());
        }
        issues
    }

    pub fn world_ports(
        local_ports: &[SymbolPort],
        origin: Vec3,
        rotation_degrees: f64,
        scale: Vec3,
    ) -> Vec<(String, Vec3)> {
        let rad = rotation_degrees.to_radians();
        let (sin, cos) = rad.sin_cos();
        local_ports
            .iter()
            .map(|p| {
                let sx = p.position.x * scale.x;
                let sy = p.position.y * scale.y;
                let rx = sx * cos - sy * sin + origin.x;
                let ry = sx * sin + sy * cos + origin.y;
                (p.name.clone(), Vec3::new(rx, ry, p.position.z * scale.z + origin.z))
            })
            .collect()
    }
}

