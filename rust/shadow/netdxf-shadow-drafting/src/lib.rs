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
