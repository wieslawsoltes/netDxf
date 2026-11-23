#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DrawingBlockEntry {
    pub name: String,
    pub block: Option<ObjectId>,
    pub description: String,
    pub base_point: Vec3,
    pub units: String,
    pub entity_count: usize,
    pub entities: usize,
    pub attributes: usize,
    pub model_space: bool,
}

pub struct DrawingBlockLibrary;

impl DrawingBlockLibrary {
    pub const MAXIMUM_BYTES: usize = 32 * 1024 * 1024;

    pub fn entries(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DrawingBlockEntry> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                let name = r.field("Name").text().to_string();
                let count = r.field("Entities").items().len();
                DrawingBlockEntry {
                    name: name.clone(),
                    block: Some(o.id.clone()),
                    description: r.field("Description").text().to_string(),
                    base_point: r.field("Origin").vector(Vec3::ZERO),
                    units: "Unitless".into(),
                    entity_count: count,
                    entities: count,
                    attributes: 0,
                    model_space: name.eq_ignore_ascii_case("*Model_Space"),
                }
            })
            .collect()
    }

    pub fn snapshot(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DrawingBlockEntry> {
        Self::entries(snapshot)
    }

    pub fn export(entries: &[DrawingBlockEntry]) -> Result<String, String> {
        let json = serde_json::to_string(entries).map_err(|e| e.to_string())?;
        if json.len() > Self::MAXIMUM_BYTES {
            return Err("Block library exceeds maximum byte budget.".into());
        }
        Ok(json)
    }

    pub fn read(payload: &str) -> Result<Vec<DrawingBlockEntry>, String> {
        if payload.len() > Self::MAXIMUM_BYTES {
            return Err("Block library exceeds maximum byte budget.".into());
        }
        serde_json::from_str(payload).map_err(|e| e.to_string())
    }

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        entries: &[DrawingBlockEntry],
    ) -> netdxf_shadow::ShadowGraphPatch {
        let mut changes = Vec::new();
        for entry in entries {
            let id = ObjectId::new(format!("O:Block:{}", entry.name));
            let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Blocks.Block");
            obj.properties.insert(
                "Name".into(),
                netdxf_shadow::ShadowValue::from_string(entry.name.clone()),
            );
            obj.properties.insert(
                "Origin".into(),
                netdxf_shadow::ShadowValue::from_vec3(entry.base_point, false),
            );
            changes.push(netdxf_shadow::ShadowObjectChange {
                id,
                before: None,
                after: Some(obj),
            });
        }
        netdxf_shadow::ShadowGraphPatch::new(snapshot.document_id.clone(), snapshot.revision, changes)
    }

    pub fn summarize(names: &[String]) -> Vec<DrawingBlockEntry> {
        names
            .iter()
            .map(|n| DrawingBlockEntry {
                name: n.clone(),
                ..Default::default()
            })
            .collect()
    }
}

