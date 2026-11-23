//! High-level CAD drafting commands, grips, layers, blocks, hatches, and object snaps (`netDxf.Shadow.Drafting`).

#![forbid(unsafe_code)]

use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingAttributeDefinition {
    pub id: ObjectId,
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeDefinitionEdit {
    pub id: ObjectId,
    pub tag: Option<String>,
    pub prompt: Option<String>,
    pub default_value: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeDefinitionCreation {
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeDefinitionChanges {
    pub additions: Vec<AttributeDefinitionCreation>,
    pub edits: Vec<AttributeDefinitionEdit>,
    pub removals: Vec<ObjectId>,
}

pub struct DraftingAttributeDefinitions;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingAttribute {
    pub id: ObjectId,
    pub tag: String,
    pub value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeEdit {
    pub id: ObjectId,
    pub value: String,
}

pub struct DraftingAttributes;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockLibraryConflict {
    #[default]
    Skip,
    Overwrite,
    Rename,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockLibraryResourceConflict {
    #[default]
    KeepTarget,
    UseSource,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryImport {
    pub block_names: Vec<String>,
    pub conflict_policy: BlockLibraryConflict,
    pub resource_policy: BlockLibraryResourceConflict,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryLimits {
    pub maximum_blocks: usize,
    pub maximum_entities: usize,
}

impl Default for BlockLibraryLimits {
    fn default() -> Self {
        Self {
            maximum_blocks: 4_096,
            maximum_entities: 200_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryResource {
    pub kind: String,
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct BlockLibraryPlan {
    pub imported_blocks: Vec<String>,
    pub resources: Vec<BlockLibraryResource>,
}

pub struct DraftingBlockLibrary;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingCommand {
    pub name: String,
    pub summary: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingTransformMeasure {
    pub translation: Vec3,
    pub rotation_radians: f64,
    pub scale: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingTransformOptions {
    pub copy: bool,
    pub uniform_scale: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLimits {
    pub maximum_entities: usize,
    pub maximum_vertices: usize,
}

impl Default for DraftingLimits {
    fn default() -> Self {
        Self {
            maximum_entities: 100_000,
            maximum_vertices: 1_000_000,
        }
    }
}

#[derive(Debug, Clone)]
pub struct DraftingPlan {
    pub command: DraftingCommand,
    pub patch: ShadowGraphPatch,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingGrip {
    pub entity: ObjectId,
    pub key: String,
    pub position: Vec3,
}

pub struct DraftingGuard;
pub struct DraftingGrips;

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
    pub pattern_type: DraftingHatchPatternType,
    pub scale: f64,
    pub angle: f64,
    pub lines: Vec<DraftingHatchLine>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct HatchAuthoringRequest {
    pub layer: String,
    pub pattern: DraftingHatchPattern,
    pub boundary: Vec<Vec3>,
}

pub struct DraftingHatches;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerIsolationEdit {
    pub keep_layers: Vec<String>,
    pub lock_others: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerIsolationStatus {
    pub isolated: bool,
    pub active_layers: Vec<String>,
}

pub struct DraftingLayerIsolation;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateEntryEdit {
    pub layer: String,
    pub hidden: Option<bool>,
    pub frozen: Option<bool>,
    pub locked: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SavedLayerProperties {
    pub hidden: bool,
    pub frozen: bool,
    pub locked: bool,
    pub plot: bool,
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateEdit {
    pub name: String,
    pub entries: Vec<LayerStateEntryEdit>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayerState {
    pub name: String,
    pub layer_count: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayer {
    pub id: ObjectId,
    pub name: String,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_locked: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerEdit {
    pub name: String,
    pub is_visible: Option<bool>,
    pub is_frozen: Option<bool>,
    pub is_locked: Option<bool>,
}

pub struct DraftingLayers;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BlockCreation {
    pub name: String,
    pub origin: Vec3,
    pub entities: Vec<ObjectId>,
}

pub struct DraftingBlocks;
pub struct DraftingPlanner;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockSourceDisposition {
    #[default]
    Retain,
    ConvertToInsert,
    Delete,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BlockDefinitionRequest {
    pub name: String,
    pub origin: Vec3,
    pub disposition: BlockSourceDisposition,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DraftingSelectionMode {
    #[default]
    Window,
    Crossing,
    Point,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ObjectSnapMode {
    #[default]
    Endpoint,
    Midpoint,
    Center,
    Intersection,
    Perpendicular,
    Nearest,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ObjectSnap {
    pub mode: ObjectSnapMode,
    pub point: Vec3,
    pub entity: Option<ObjectId>,
}

pub struct DraftingQueries;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ResourceEdit {
    pub name: String,
    pub description: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypeSegmentEdit {
    pub length: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LinetypeEdit {
    pub name: String,
    pub description: String,
    pub segments: Vec<LinetypeSegmentEdit>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextStyleEdit {
    pub name: String,
    pub font_file: String,
    pub height: f64,
    pub width_factor: f64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingResourceStatus {
    pub name: String,
    pub in_use: bool,
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
    pub height: f64,
}

pub struct DraftingResources;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingArc {
    pub center: Vec3,
    pub radius: f64,
    pub start_angle: f64,
    pub end_angle: f64,
}

pub struct PlanarGeometry;

impl PlanarGeometry {
    /// Computes the intersection point of two 2D line segments `a0-a1` and `b0-b1`, if any.
    pub fn segment_intersection_2d(a0: Vec3, a1: Vec3, b0: Vec3, b1: Vec3) -> Option<Vec3> {
        let r = Vec3::xy(a1.x - a0.x, a1.y - a0.y);
        let s = Vec3::xy(b1.x - b0.x, b1.y - b0.y);
        let denom = r.x * s.y - r.y * s.x;
        if denom.abs() < 1e-12 {
            return None;
        }
        let qp = Vec3::xy(b0.x - a0.x, b0.y - a0.y);
        let t = (qp.x * s.y - qp.y * s.x) / denom;
        let u = (qp.x * r.y - qp.y * r.x) / denom;
        if (-1e-9..=1.0 + 1e-9).contains(&t) && (-1e-9..=1.0 + 1e-9).contains(&u) {
            Some(Vec3::xy(a0.x + t * r.x, a0.y + t * r.y))
        } else {
            None
        }
    }

    /// Shortest distance from point `p` to 2D segment `a-b`.
    pub fn distance_to_segment_2d(p: Vec3, a: Vec3, b: Vec3) -> f64 {
        let ab = Vec3::xy(b.x - a.x, b.y - a.y);
        let len_sq = ab.x * ab.x + ab.y * ab.y;
        if len_sq <= 1e-18 {
            return Vec3::distance(Vec3::xy(p.x, p.y), Vec3::xy(a.x, a.y));
        }
        let t = (((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len_sq).clamp(0.0, 1.0);
        let proj = Vec3::xy(a.x + t * ab.x, a.y + t * ab.y);
        Vec3::distance(Vec3::xy(p.x, p.y), proj)
    }

    /// Signed area of a simple 2D polygon (positive for counter-clockwise winding).
    pub fn signed_polygon_area(pts: &[Vec3]) -> f64 {
        if pts.len() < 3 {
            return 0.0;
        }
        let mut sum = 0.0;
        for i in 0..pts.len() {
            let a = pts[i];
            let b = pts[(i + 1) % pts.len()];
            sum += a.x * b.y - b.x * a.y;
        }
        0.5 * sum
    }
}

impl DraftingAttributeDefinitions {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttributeDefinition> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "AttributeDefinition")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingAttributeDefinition {
                    id: o.id.clone(),
                    tag: r.field("Tag").text().to_string(),
                    prompt: r.field("Prompt").text().to_string(),
                    default_value: r.field("Value").text().to_string(),
                    position: r.field("Position").vector(Vec3::ZERO),
                }
            })
            .collect()
    }
}

impl DraftingAttributes {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttribute> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Attribute")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingAttribute {
                    id: o.id.clone(),
                    tag: r.field("Tag").text().to_string(),
                    value: r.field("Value").text().to_string(),
                    position: r.field("Position").vector(Vec3::ZERO),
                }
            })
            .collect()
    }
}

impl DraftingBlockLibrary {
    pub fn plan_import(request: &BlockLibraryImport, limits: &BlockLibraryLimits) -> BlockLibraryPlan {
        let imported_blocks = request
            .block_names
            .iter()
            .take(limits.maximum_blocks)
            .cloned()
            .collect();
        BlockLibraryPlan {
            imported_blocks,
            resources: Vec::new(),
        }
    }
}

impl DraftingGuard {
    pub fn validate_limits(entity_count: usize, limits: &DraftingLimits) -> Result<(), String> {
        if entity_count > limits.maximum_entities {
            Err("Entity count exceeds drafting limits.".into())
        } else {
            Ok(())
        }
    }
}

impl DraftingGrips {
    pub fn extract(snapshot: &netdxf_shadow::ShadowSnapshot, entity: &ObjectId) -> Vec<DraftingGrip> {
        let r = snapshot.read(entity.clone());
        let mut grips = Vec::new();
        for key in ["StartPoint", "EndPoint", "Center", "Position"] {
            let f = r.field(key);
            if f.exists() {
                grips.push(DraftingGrip {
                    entity: entity.clone(),
                    key: key.to_string(),
                    position: f.vector(Vec3::ZERO),
                });
            }
        }
        grips
    }
}

impl DraftingHatches {
    pub fn solid_pattern() -> DraftingHatchPattern {
        DraftingHatchPattern {
            name: "SOLID".into(),
            pattern_type: DraftingHatchPatternType::Solid,
            scale: 1.0,
            angle: 0.0,
            lines: Vec::new(),
        }
    }
}

impl DraftingLayerIsolation {
    pub fn isolate(edit: &LayerIsolationEdit) -> LayerIsolationStatus {
        LayerIsolationStatus {
            isolated: !edit.keep_layers.is_empty(),
            active_layers: edit.keep_layers.clone(),
        }
    }
}

impl DraftingLayerStates {
    pub fn capture(snapshot: &netdxf_shadow::ShadowSnapshot, name: impl Into<String>) -> DraftingLayerStateDetails {
        let mut layers = BTreeMap::new();
        for l in DraftingLayers::list(snapshot) {
            layers.insert(
                l.name,
                SavedLayerProperties {
                    hidden: !l.is_visible,
                    frozen: l.is_frozen,
                    locked: l.is_locked,
                    plot: true,
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

impl DraftingLayers {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingLayer> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Layer")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingLayer {
                    id: o.id.clone(),
                    name: r.field("Name").text().to_string(),
                    is_visible: r.field("IsVisible").boolean(true),
                    is_frozen: r.field("IsFrozen").boolean(false),
                    is_locked: r.field("IsLocked").boolean(false),
                }
            })
            .collect()
    }
}

impl DraftingBlocks {
    pub fn list_names(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<String> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .map(|o| snapshot.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }
}

impl DraftingPlanner {
    pub fn empty_plan(snapshot: &netdxf_shadow::ShadowSnapshot, name: impl Into<String>) -> DraftingPlan {
        DraftingPlan {
            command: DraftingCommand {
                name: name.into(),
                summary: String::new(),
            },
            patch: ShadowGraphPatch::new(snapshot.document_id.clone(), snapshot.revision, Vec::new()),
        }
    }
}

impl DraftingQueries {
    pub fn select_in_bounds(
        scene: &netdxf_shadow_geometry::CadScene,
        region: netdxf_shadow::Bounds2,
        mode: DraftingSelectionMode,
    ) -> Vec<ObjectId> {
        scene
            .occurrences
            .iter()
            .filter(|occ| match mode {
                DraftingSelectionMode::Window => {
                    !occ.bounds.is_empty()
                        && occ.bounds.min_x >= region.min_x
                        && occ.bounds.max_x <= region.max_x
                        && occ.bounds.min_y >= region.min_y
                        && occ.bounds.max_y <= region.max_y
                }
                DraftingSelectionMode::Crossing | DraftingSelectionMode::Point => {
                    occ.bounds.intersects(region)
                }
            })
            .map(|occ| occ.entity.clone())
            .collect()
    }
}

impl DraftingResources {
    pub fn linetype_statuses(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingResourceStatus> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Linetype")
            .map(|o| DraftingResourceStatus {
                name: snapshot.read(o.id.clone()).field("Name").text().to_string(),
                in_use: true,
            })
            .collect()
    }
}
