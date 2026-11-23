#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateEntryEdit {
    pub name: String,
    pub layer: String,
    pub visible: Option<bool>,
    pub hidden: Option<bool>,
    pub frozen: Option<bool>,
    pub locked: Option<bool>,
    pub frozen_in_new_viewports: Option<bool>,
    pub plot: Option<bool>,
    pub color: Option<String>,
    pub color_index: Option<i16>,
    pub linetype: Option<String>,
    pub lineweight: Option<i16>,
    pub transparency: Option<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SavedLayerProperties {
    pub name: String,
    pub flags: i32,
    pub hidden: bool,
    pub frozen: bool,
    pub locked: bool,
    pub plot: bool,
    pub color: Option<String>,
    pub color_index: i16,
    pub true_color: Option<u32>,
    pub linetype: String,
    pub lineweight: i16,
    pub transparency: u8,
    pub stored_alpha_value: Option<i32>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DraftingLayerStateDetails {
    pub name: String,
    pub description: String,
    pub layers: BTreeMap<String, SavedLayerProperties>,
}

pub struct DraftingLayerStates;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateTransfer {
    pub name: String,
    pub details: DraftingLayerStateDetails,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateResourceMap {
    pub linetype_map: BTreeMap<String, String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateImportOptions {
    pub overwrite_existing: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateImportConflict {
    pub name: String,
    pub reason: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateImportAnalysis {
    pub conflicts: Vec<LayerStateImportConflict>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LayerStateFields {
    #[default]
    All,
    VisibilityOnly,
    AppearanceOnly,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateEdit {
    pub id: Option<ObjectId>,
    pub action: String,
    pub name: String,
    pub description: Option<String>,
    pub current_layer: Option<String>,
    pub fields: LayerStateFields,
    pub restore_current_layer: bool,
    pub turn_off_unlisted_layers: bool,
    pub add_layers: Vec<LayerStateEntryEdit>,
    pub remove_layers: Vec<String>,
    pub entries: Vec<LayerStateEntryEdit>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayerState {
    pub id: ObjectId,
    pub name: String,
    pub description: String,
    pub current_layer: String,
    pub paper_space: bool,
    pub layers: Vec<String>,
    pub fields: LayerStateFields,
    pub restore_mask: i32,
    pub layer_count: usize,
}

impl DraftingLayerStates {
    pub const SUPPORTED_MASK: i32 = 1 | 2 | 4 | 8 | 16 | 64 | 128 | 256 | 512;

    pub fn fields_from_mask(mask: i32) -> LayerStateFields {
        let supported = mask & Self::SUPPORTED_MASK;
        if supported == Self::SUPPORTED_MASK {
            LayerStateFields::All
        } else if (supported & (64 | 128 | 256 | 512)) == 0 {
            LayerStateFields::VisibilityOnly
        } else if (supported & (1 | 2 | 4 | 8 | 16)) == 0 {
            LayerStateFields::AppearanceOnly
        } else {
            LayerStateFields::All
        }
    }

    pub fn mask_from_fields(fields: LayerStateFields) -> i32 {
        match fields {
            LayerStateFields::All => Self::SUPPORTED_MASK,
            LayerStateFields::VisibilityOnly => 1 | 2 | 4 | 8 | 16,
            LayerStateFields::AppearanceOnly => 64 | 128 | 256 | 512,
        }
    }

    pub fn read(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingLayerState> {
        let mut out = Vec::new();
        for (id, obj) in &snapshot.objects {
            if obj.type_name.contains("LayerState") && !obj.type_name.contains("Properties") {
                let reader = snapshot.read(id.clone());
                let name_field = reader.field("Name");
                let name_txt = name_field.text();
                let name = if name_txt.is_empty() {
                    id.as_str().to_string()
                } else {
                    name_txt.to_string()
                };
                let description = reader.field("Description").text().to_string();
                let cur_field = reader.field("CurrentLayer");
                let cur_txt = cur_field.text();
                let current_layer = if cur_txt.is_empty() {
                    "0".to_string()
                } else {
                    cur_txt.to_string()
                };
                let paper_space = reader.field("PaperSpace").boolean(false);
                let restore_mask = reader.field("RestoreMask").number(2047.0) as i32;
                let layers: Vec<String> = match obj.get("Properties") {
                    netdxf_shadow::ShadowValue::Sequence(seq) => seq
                        .items
                        .iter()
                        .filter_map(|item| match item {
                            netdxf_shadow::ShadowValue::Atom(a) => Some(a.text.clone()),
                            _ => None,
                        })
                        .collect(),
                    _ => Vec::new(),
                };
                let layer_count = layers.len();
                out.push(DraftingLayerState {
                    id: id.clone(),
                    name,
                    description,
                    current_layer,
                    paper_space,
                    layers,
                    fields: Self::fields_from_mask(restore_mask),
                    restore_mask,
                    layer_count,
                });
            }
        }
        out.sort_by(|a, b| a.name.to_lowercase().cmp(&b.name.to_lowercase()));
        out
    }

    pub fn details(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        id: &ObjectId,
    ) -> Result<DraftingLayerStateDetails, String> {
        if !snapshot.objects.contains_key(id) {
            return Err("Unknown layer state.".to_string());
        }
        let reader = snapshot.read(id.clone());
        let name = reader.field("Name").text().to_string();
        let description = reader.field("Description").text().to_string();
        let mut details = Self::capture(snapshot, name);
        details.description = description;
        Ok(details)
    }

    pub fn export(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        id: &ObjectId,
    ) -> Result<LayerStateTransfer, String> {
        let details = Self::details(snapshot, id)?;
        Ok(LayerStateTransfer {
            name: details.name.clone(),
            details,
        })
    }

    pub fn analyze_import(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        source: &LayerStateTransfer,
        options: &LayerStateImportOptions,
    ) -> LayerStateImportAnalysis {
        let mut conflicts = Vec::new();
        let existing_states = Self::read(snapshot);
        let exists = existing_states
            .iter()
            .any(|s| s.name.eq_ignore_ascii_case(&source.name));
        if exists && !options.overwrite_existing {
            conflicts.push(LayerStateImportConflict {
                name: source.name.clone(),
                reason: "A state with this name exists. Choose a new name or explicitly replace it.".to_string(),
            });
        }
        let existing_layers: std::collections::BTreeSet<String> = DraftingLayers::list(snapshot)
            .into_iter()
            .map(|l| l.name.to_ascii_lowercase())
            .collect();
        for layer_name in source.details.layers.keys() {
            if !existing_layers.contains(&layer_name.to_ascii_lowercase()) {
                conflicts.push(LayerStateImportConflict {
                    name: layer_name.clone(),
                    reason: format!("Missing layer '{layer_name}'."),
                });
            }
        }
        LayerStateImportAnalysis { conflicts }
    }

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        request: &LayerStateEdit,
    ) -> Result<DraftingPlan, String> {
        let mut changes = Vec::new();
        match request.action.as_str() {
            "save" => {
                let id = request
                    .id
                    .clone()
                    .unwrap_or_else(|| ObjectId::new(format!("LS_{}", snapshot.objects.len() + 1)));
                let mut obj =
                    netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Objects.LayerState");
                obj.properties.insert(
                    "Name".to_string(),
                    netdxf_shadow::ShadowValue::from_string(request.name.clone()),
                );
                if let Some(desc) = &request.description {
                    obj.properties.insert(
                        "Description".to_string(),
                        netdxf_shadow::ShadowValue::from_string(desc.clone()),
                    );
                }
                obj.properties.insert(
                    "RestoreMask".to_string(),
                    netdxf_shadow::ShadowValue::from_f64(Self::mask_from_fields(request.fields) as f64),
                );
                let before = snapshot.objects.get(&id).map(|o| (**o).clone());
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id,
                    before,
                    after: Some(obj),
                });
            }
            "rename" => {
                let id = request
                    .id
                    .clone()
                    .ok_or_else(|| "Choose an existing layer state.".to_string())?;
                let existing = snapshot
                    .objects
                    .get(&id)
                    .ok_or_else(|| "Unknown layer state.".to_string())?;
                let mut updated = (**existing).clone();
                updated.properties.insert(
                    "Name".to_string(),
                    netdxf_shadow::ShadowValue::from_string(request.name.clone()),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id,
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
            "delete" => {
                let id = request
                    .id
                    .clone()
                    .ok_or_else(|| "Choose an existing layer state.".to_string())?;
                let existing = snapshot
                    .objects
                    .get(&id)
                    .ok_or_else(|| "Unknown layer state.".to_string())?;
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id,
                    before: Some((**existing).clone()),
                    after: None,
                });
            }
            "restore" | "options" | "edit" => {
                let id = request
                    .id
                    .clone()
                    .ok_or_else(|| "Choose an existing layer state.".to_string())?;
                let existing = snapshot
                    .objects
                    .get(&id)
                    .ok_or_else(|| "Unknown layer state.".to_string())?;
                let mut updated = (**existing).clone();
                updated.properties.insert(
                    "RestoreMask".to_string(),
                    netdxf_shadow::ShadowValue::from_f64(Self::mask_from_fields(request.fields) as f64),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id,
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
            _ => {
                return Err(
                    "Use save, rename, delete, restore, options or edit for a layer state."
                        .to_string(),
                );
            }
        }
        Ok(DraftingPlan {
            command: DraftingCommand {
                name: format!("{} LAYER STATE", request.action.to_ascii_uppercase()),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(snapshot.document_id.clone(), snapshot.revision, changes),
        })
    }

    pub fn plan_import(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        source: &LayerStateTransfer,
        options: &LayerStateImportOptions,
    ) -> Result<DraftingPlan, String> {
        let analysis = Self::analyze_import(snapshot, source, options);
        if !analysis.conflicts.is_empty() {
            let msg = analysis
                .conflicts
                .iter()
                .map(|c| c.reason.as_str())
                .collect::<Vec<_>>()
                .join(" ");
            return Err(msg);
        }
        let edit = LayerStateEdit {
            action: "save".to_string(),
            name: source.name.clone(),
            description: Some(source.details.description.clone()),
            ..Default::default()
        };
        let mut plan = Self::plan(snapshot, &edit)?;
        plan.command.name = "IMPORT LAYER STATE".to_string();
        Ok(plan)
    }

    pub fn capture(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        name: impl Into<String>,
    ) -> DraftingLayerStateDetails {
        let mut layers = BTreeMap::new();
        for l in DraftingLayers::list(snapshot) {
            layers.insert(
                l.name.clone(),
                SavedLayerProperties {
                    name: l.name,
                    flags: 0,
                    hidden: !l.is_visible,
                    frozen: l.is_frozen,
                    locked: l.is_locked,
                    plot: l.plot,
                    color: l.color,
                    color_index: l.color_index,
                    true_color: l.true_color,
                    linetype: l.linetype,
                    lineweight: l.lineweight,
                    transparency: l.transparency,
                    stored_alpha_value: None,
                },
            );
        }
        DraftingLayerStateDetails {
            name: name.into(),
            description: String::new(),
            layers,
        }
    }
}
