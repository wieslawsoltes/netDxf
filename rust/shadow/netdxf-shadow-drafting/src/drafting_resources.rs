#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ResourceEdit {
    pub id: Option<ObjectId>,
    pub action: String,
    pub kind: String,
    pub name: String,
    pub description: Option<String>,
    pub linetype: Option<LinetypeEdit>,
    pub text_style: Option<TextStyleEdit>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct LinetypeSegmentEdit {
    pub kind: String,
    pub length: f64,
    pub text: Option<String>,
    pub style: Option<String>,
    pub shape_number: Option<i16>,
    pub scale: f64,
    pub rotation: f64,
    pub rotation_type: String,
    pub offset_x: f64,
    pub offset_y: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LinetypeEdit {
    pub name: String,
    pub description: String,
    pub segments: Vec<LinetypeSegmentEdit>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct TextStyleEdit {
    pub name: String,
    pub font_file: String,
    pub big_font: Option<String>,
    pub font_family: Option<String>,
    pub font_style: Option<String>,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub backward: bool,
    pub upside_down: bool,
    pub vertical: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DraftingResourceStatus {
    pub id: Option<ObjectId>,
    pub name: String,
    pub in_use: bool,
    pub current: bool,
    pub reserved: bool,
    pub uses: usize,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingLinetype {
    pub name: String,
    pub description: String,
    pub length: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingTextStyle {
    pub name: String,
    pub font_file: String,
    pub big_font: String,
    pub font_family: String,
    pub font_style: String,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub backward: bool,
    pub upside_down: bool,
    pub vertical: bool,
    pub status: DraftingResourceStatus,
}

pub struct DraftingResources;

impl DraftingResources {
    pub fn current(snapshot: &netdxf_shadow::ShadowSnapshot, kind: &str) -> String {
        let key = match kind.to_ascii_lowercase().as_str() {
            "layer" => "CLayer",
            "linetype" => "CeLtype",
            "textstyle" | "style" => "TextStyle",
            _ => return "0".to_string(),
        };
        for obj in snapshot.objects.values() {
            if obj.short_type() == "HeaderVariables" || obj.short_type() == "DrawingVariables" {
                let r = snapshot.read(obj.id.clone());
                let field = r.field(key);
                let txt = field.text();
                if !txt.is_empty() {
                    return txt.to_string();
                }
            }
        }
        if kind.eq_ignore_ascii_case("layer") {
            "0".to_string()
        } else {
            "Standard".to_string()
        }
    }

    pub fn reserved(kind: &str) -> Vec<&'static str> {
        match kind.to_ascii_lowercase().as_str() {
            "layer" => vec!["0", "Defpoints"],
            "linetype" => vec!["ByLayer", "ByBlock", "Continuous"],
            "textstyle" | "style" => vec!["Standard"],
            _ => Vec::new(),
        }
    }

    pub fn linetypes(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingLinetype> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Linetype")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingLinetype {
                    name: r.field("Name").text().to_string(),
                    description: r.field("Description").text().to_string(),
                    length: r.field("Length").number(0.0),
                }
            })
            .collect()
    }

    pub fn text_styles(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingTextStyle> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "TextStyle")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                let name = r.field("Name").text().to_string();
                DraftingTextStyle {
                    name: name.clone(),
                    font_file: r.field("FontFile").text().to_string(),
                    big_font: r.field("BigFont").text().to_string(),
                    font_family: r.field("FontFamilyName").text().to_string(),
                    font_style: r.field("FontStyle").text().to_string(),
                    height: r.field("Height").number(0.0),
                    width_factor: r.field("WidthFactor").number(1.0),
                    oblique_angle: r.field("ObliqueAngle").number(0.0),
                    backward: r.field("IsBackward").boolean(false),
                    upside_down: r.field("IsUpsideDown").boolean(false),
                    vertical: r.field("IsVertical").boolean(false),
                    status: DraftingResourceStatus {
                        name,
                        in_use: true,
                        ..Default::default()
                    },
                }
            })
            .collect()
    }

    pub fn uses(snapshot: &netdxf_shadow::ShadowSnapshot, kind: &str, name: &str) -> usize {
        let field = match kind.to_ascii_lowercase().as_str() {
            "layer" => "Layer",
            "linetype" => "Linetype",
            "textstyle" | "style" => "Style",
            _ => return 0,
        };
        snapshot
            .objects
            .values()
            .filter(|o| {
                let r = snapshot.read(o.id.clone());
                let f = r.field(field);
                f.text().eq_ignore_ascii_case(name)
            })
            .count()
    }

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        edit: &ResourceEdit,
    ) -> DraftingPlan {
        DraftingPlanner::empty_plan(snapshot, format!("RESOURCE {}", edit.name))
    }

    pub fn linetype_statuses(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingResourceStatus> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Linetype")
            .map(|o| DraftingResourceStatus {
                name: snapshot.read(o.id.clone()).field("Name").text().to_string(),
                in_use: true,
                ..Default::default()
            })
            .collect()
    }
}
