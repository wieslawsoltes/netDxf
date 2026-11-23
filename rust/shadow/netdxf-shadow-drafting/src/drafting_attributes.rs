#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingAttribute {
    pub id: ObjectId,
    pub tag: String,
    pub value: String,
    pub position: Vec3,
    pub layer: String,
    pub color: Option<i16>,
    pub linetype: String,
    pub text_style: String,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub rotation: f64,
    pub alignment: String,
    pub backward: bool,
    pub upside_down: bool,
    pub invisible: bool,
    pub constant: bool,
    pub verify: bool,
    pub preset: bool,
    pub multiline: bool,
    pub position_locked: bool,
    pub line_spacing: f64,
    pub exact_spacing: bool,
    pub duplicate_tag: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeEdit {
    pub id: ObjectId,
    pub attribute: Option<ObjectId>,
    pub value: String,
    pub position: Option<Vec3>,
    pub layer: Option<String>,
    pub color: Option<i16>,
    pub linetype: Option<String>,
    pub text_style: Option<String>,
    pub height: Option<f64>,
    pub width_factor: Option<f64>,
    pub oblique_angle: Option<f64>,
    pub rotation: Option<f64>,
    pub alignment: Option<String>,
    pub backward: Option<bool>,
    pub upside_down: Option<bool>,
    pub invisible: Option<bool>,
    pub multiline: Option<bool>,
    pub line_spacing: Option<f64>,
    pub exact_spacing: Option<bool>,
}

pub struct DraftingAttributes;

impl DraftingAttributes {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttribute> {
        let mut tag_counts: BTreeMap<String, usize> = BTreeMap::new();
        for o in snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Attribute")
        {
            let tag = snapshot.read(o.id.clone()).field("Tag").text().to_uppercase();
            *tag_counts.entry(tag).or_insert(0) += 1;
        }
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Attribute")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                let tag = r.field("Tag").text().to_string();
                let dup = tag_counts
                    .get(&tag.to_uppercase())
                    .copied()
                    .unwrap_or(0)
                    > 1;
                DraftingAttribute {
                    id: o.id.clone(),
                    tag,
                    value: r.field("Value").text().to_string(),
                    position: r.field("Position").vector(Vec3::ZERO),
                    layer: r.field("Layer").text().to_string(),
                    color: Some(r.field("Color").number(256.0) as i16),
                    linetype: r.field("Linetype").text().to_string(),
                    text_style: r.field("Style").text().to_string(),
                    height: r.field("Height").number(1.0),
                    width_factor: r.field("WidthFactor").number(1.0),
                    oblique_angle: r.field("ObliqueAngle").number(0.0),
                    rotation: r.field("Rotation").number(0.0),
                    alignment: r.field("Alignment").text().to_string(),
                    backward: r.field("IsBackward").boolean(false),
                    upside_down: r.field("IsUpsideDown").boolean(false),
                    invisible: r.field("Invisible").boolean(false),
                    constant: r.field("Constant").boolean(false),
                    verify: r.field("Verify").boolean(false),
                    preset: r.field("Preset").boolean(false),
                    multiline: r.field("Multiline").boolean(false),
                    position_locked: r.field("PositionLocked").boolean(false),
                    line_spacing: r.field("LineSpacing").number(1.0),
                    exact_spacing: r.field("ExactSpacing").boolean(false),
                    duplicate_tag: dup,
                }
            })
            .collect()
    }
}
