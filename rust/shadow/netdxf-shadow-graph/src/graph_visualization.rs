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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphVisualizationOptions {
    pub offset: usize,
    pub limit: usize,
}

impl Default for GraphVisualizationOptions {
    fn default() -> Self {
        Self {
            offset: 0,
            limit: 1_000,
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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphVisualizationEdge {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub start: Vec3,
    pub end: Vec3,
    pub length: f64,
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
    pub nodes: GraphVisualizationPage<GraphVisualizationNode>,
    pub edges: GraphVisualizationPage<GraphVisualizationEdge>,
}

