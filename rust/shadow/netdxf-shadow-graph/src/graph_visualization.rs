#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::{BTreeMap, BTreeSet, VecDeque};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphVisualizationOptions {
    pub offset: usize,
    pub limit: usize,
    pub node_offset: usize,
    pub node_limit: usize,
    pub edge_offset: usize,
    pub edge_limit: usize,
    pub component: Option<usize>,
    pub entity: Option<String>,
    pub root_entity: Option<String>,
    pub occurrence: Option<String>,
}

impl Default for GraphVisualizationOptions {
    fn default() -> Self {
        Self {
            offset: 0,
            limit: 1_000,
            node_offset: 0,
            node_limit: 1_000,
            edge_offset: 0,
            edge_limit: 1_000,
            component: None,
            entity: None,
            root_entity: None,
            occurrence: None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphVisualizationNode {
    pub id: usize,
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub degree: usize,
    pub component: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct GraphVisualizationEdge {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub start: Vec3,
    pub end: Vec3,
    pub length: f64,
    pub component: usize,
    pub bridge: bool,
    pub entity: Option<ObjectId>,
    pub root_entity: Option<ObjectId>,
    pub occurrence: String,
    pub insert_path: Vec<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphVisualizationPage<T> {
    pub total: usize,
    pub offset: usize,
    pub items: Vec<T>,
    pub next_offset: Option<usize>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphVisualization {
    pub node_count: usize,
    pub edge_count: usize,
    pub component_count: usize,
    pub cycle_rank: usize,
    pub articulation_point_count: usize,
    pub bridge_count: usize,
    pub dead_end_count: usize,
    pub isolated_node_count: usize,
    pub junction_count: usize,
    pub diagnostic_count: usize,
    pub total_length: f64,
    pub node_limit: usize,
    pub edge_limit: usize,
    pub truncated: bool,
    pub nodes: GraphVisualizationPage<GraphVisualizationNode>,
    pub edges: GraphVisualizationPage<GraphVisualizationEdge>,
}

