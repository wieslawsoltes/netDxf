#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingHatchLine {
    pub angle: f64,
    pub origin: Vec3,
    pub delta: Vec3,
    pub dashes: Vec<f64>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DraftingHatchPatternType {
    #[default]
    Solid,
    Predefined,
    UserDefined,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingHatchPattern {
    pub name: String,
    pub description: String,
    pub pattern_type: DraftingHatchPatternType,
    pub r#type: DraftingHatchPatternType,
    pub scale: f64,
    pub angle: f64,
    pub is_double: bool,
    pub islands: bool,
    pub origin: Vec3,
    pub lines: Vec<DraftingHatchLine>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct HatchAuthoringRequest {
    pub layer: String,
    pub pattern: DraftingHatchPattern,
    pub boundary: Vec<Vec3>,
}

impl HatchAuthoringRequest {
    pub fn boundaries(&self) -> &[Vec3] {
        &self.boundary
    }

    pub fn polygons(&self) -> &[Vec3] {
        &self.boundary
    }
}

pub struct DraftingHatches;

impl DraftingHatches {
    pub fn solid_pattern() -> DraftingHatchPattern {
        DraftingHatchPattern {
            name: "SOLID".into(),
            pattern_type: DraftingHatchPatternType::Solid,
            r#type: DraftingHatchPatternType::Solid,
            scale: 1.0,
            angle: 0.0,
            lines: Vec::new(),
            ..Default::default()
        }
    }

    pub fn pattern_names() -> Vec<&'static str> {
        vec!["SOLID", "LINE", "NET", "DOTS", "ANSI31", "ANSI32", "ANSI37"]
    }

    pub fn resolve_pattern(name: &str, scale: f64, angle: f64) -> DraftingHatchPattern {
        let upper = name.trim().to_ascii_uppercase();
        if upper.is_empty() || upper == "SOLID" {
            return Self::solid_pattern();
        }
        let mut lines = Vec::new();
        if upper == "ANSI31" || upper == "LINE" {
            lines.push(DraftingHatchLine {
                angle: 45.0 + angle,
                origin: Vec3::ZERO,
                delta: Vec3::xy(0.0, 3.175 * scale.max(1e-6)),
                dashes: Vec::new(),
            });
        } else if upper == "NET" || upper == "ANSI37" {
            lines.push(DraftingHatchLine {
                angle,
                origin: Vec3::ZERO,
                delta: Vec3::xy(0.0, 3.175 * scale.max(1e-6)),
                dashes: Vec::new(),
            });
            lines.push(DraftingHatchLine {
                angle: 90.0 + angle,
                origin: Vec3::ZERO,
                delta: Vec3::xy(0.0, 3.175 * scale.max(1e-6)),
                dashes: Vec::new(),
            });
        }
        DraftingHatchPattern {
            name: upper,
            pattern_type: DraftingHatchPatternType::Predefined,
            r#type: DraftingHatchPatternType::Predefined,
            scale: scale.max(1e-6),
            angle,
            lines,
            ..Default::default()
        }
    }

    pub fn read_pattern(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        hatch_id: &ObjectId,
    ) -> Option<DraftingHatchPattern> {
        if !snapshot.objects.contains_key(hatch_id) {
            return None;
        }
        let r = snapshot.read(hatch_id.clone());
        let name = r.field("PatternName").text().to_string();
        let scale = r.field("PatternScale").number(1.0);
        let angle = r.field("PatternAngle").number(0.0);
        Some(Self::resolve_pattern(&name, scale, angle))
    }

    pub fn create(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        request: &HatchAuthoringRequest,
    ) -> DraftingPlan {
        let id = ObjectId::new(format!("HATCH_{}", snapshot.objects.len() + 1));
        let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Entities.Hatch");
        obj.properties.insert(
            "Layer".into(),
            netdxf_shadow::ShadowValue::from_string(request.layer.clone()),
        );
        obj.properties.insert(
            "PatternName".into(),
            netdxf_shadow::ShadowValue::from_string(request.pattern.name.clone()),
        );
        obj.properties.insert(
            "PatternScale".into(),
            netdxf_shadow::ShadowValue::from_f64(request.pattern.scale),
        );
        obj.properties.insert(
            "PatternAngle".into(),
            netdxf_shadow::ShadowValue::from_f64(request.pattern.angle),
        );
        DraftingPlan {
            command: DraftingCommand {
                name: "CREATE HATCH".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![netdxf_shadow::ShadowObjectChange {
                    id,
                    before: None,
                    after: Some(obj),
                }],
            ),
        }
    }

    pub fn create_in_definition(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        _block_name: &str,
        request: &HatchAuthoringRequest,
    ) -> DraftingPlan {
        Self::create(snapshot, request)
    }

    pub fn change_pattern(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        hatch_id: &ObjectId,
        pattern: &DraftingHatchPattern,
    ) -> Result<DraftingPlan, String> {
        let existing = snapshot
            .objects
            .get(hatch_id)
            .ok_or_else(|| "Unknown hatch entity.".to_string())?;
        let mut updated = (**existing).clone();
        updated.properties.insert(
            "PatternName".into(),
            netdxf_shadow::ShadowValue::from_string(pattern.name.clone()),
        );
        updated.properties.insert(
            "PatternScale".into(),
            netdxf_shadow::ShadowValue::from_f64(pattern.scale),
        );
        updated.properties.insert(
            "PatternAngle".into(),
            netdxf_shadow::ShadowValue::from_f64(pattern.angle),
        );
        Ok(DraftingPlan {
            command: DraftingCommand {
                name: "CHANGE HATCH PATTERN".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![netdxf_shadow::ShadowObjectChange {
                    id: hatch_id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                }],
            ),
        })
    }
}

