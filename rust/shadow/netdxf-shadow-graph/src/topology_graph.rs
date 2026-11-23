use netdxf_shadow::{ObjectId, Vec3};
use netdxf_shadow_geometry::{CadScene, CurveSampler, SamplingOptions};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, VecDeque};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphNode {
    pub id: usize,
    pub position: Vec3,
    pub edges: Vec<usize>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphEdge {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub length: f64,
    pub entity: ObjectId,
    pub occurrence: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphPath {
    pub nodes: Vec<usize>,
    pub edges: Vec<usize>,
    pub length: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphVisit {
    pub node: usize,
    pub depth: usize,
    pub via_edge: Option<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphTraversal {
    pub visits: Vec<GraphVisit>,
    pub node_limit_reached: bool,
    pub depth_limit_reached: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphCycle {
    pub id: usize,
    pub closing_edge: usize,
    pub start: usize,
    pub ancestor: usize,
    pub edge_count: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct GraphCycleBasis {
    pub cycles: Vec<GraphCycle>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphSpanningForest {
    pub edges: Vec<usize>,
    pub component_count: usize,
    pub length: f64,
}

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

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct TopologyOptions {
    pub endpoint_tolerance: f64,
    pub curve_tolerance: f64,
}

impl Default for TopologyOptions {
    fn default() -> Self {
        Self {
            endpoint_tolerance: 0.01,
            curve_tolerance: 0.05,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TopologyGraph {
    pub document_id: String,
    pub revision: i64,
    pub nodes: Vec<GraphNode>,
    pub edges: Vec<GraphEdge>,
}

impl TopologyGraph {
    pub fn bfs(&self, start: usize, max_depth: usize, max_nodes: usize) -> GraphTraversal {
        if start >= self.nodes.len() {
            return GraphTraversal {
                visits: Vec::new(),
                node_limit_reached: false,
                depth_limit_reached: false,
            };
        }
        let mut visited = vec![false; self.nodes.len()];
        let mut queue = VecDeque::new();
        let mut visits = Vec::new();
        let mut node_limit_reached = false;
        let mut depth_limit_reached = false;

        visited[start] = true;
        queue.push_back((start, 0usize, None));

        while let Some((u, depth, via)) = queue.pop_front() {
            if visits.len() >= max_nodes {
                node_limit_reached = true;
                break;
            }
            visits.push(GraphVisit {
                node: u,
                depth,
                via_edge: via,
            });
            if depth >= max_depth {
                depth_limit_reached = true;
                continue;
            }
            for &ei in &self.nodes[u].edges {
                let e = &self.edges[ei];
                let v = if e.a == u { e.b } else { e.a };
                if !visited[v] {
                    visited[v] = true;
                    queue.push_back((v, depth + 1, Some(ei)));
                }
            }
        }

        GraphTraversal {
            visits,
            node_limit_reached,
            depth_limit_reached,
        }
    }

    pub fn spanning_forest(&self) -> GraphSpanningForest {
        let n = self.nodes.len();
        let mut visited = vec![false; n];
        let mut forest_edges = Vec::new();
        let mut total_len = 0.0;
        let mut components = 0usize;

        for start in 0..n {
            if visited[start] {
                continue;
            }
            components += 1;
            visited[start] = true;
            let mut queue = VecDeque::new();
            queue.push_back(start);
            while let Some(u) = queue.pop_front() {
                for &ei in &self.nodes[u].edges {
                    let e = &self.edges[ei];
                    let v = if e.a == u { e.b } else { e.a };
                    if !visited[v] {
                        visited[v] = true;
                        forest_edges.push(ei);
                        total_len += e.length;
                        queue.push_back(v);
                    }
                }
            }
        }

        GraphSpanningForest {
            edges: forest_edges,
            component_count: components,
            length: total_len,
        }
    }
}

/// Builds a [`TopologyGraph`] from a [`CadScene`].
pub struct TopologyBuilder;

impl TopologyBuilder {
    pub fn build(scene: &CadScene, options: &TopologyOptions) -> TopologyGraph {
        let mut nodes: Vec<GraphNode> = Vec::new();
        let mut edges: Vec<GraphEdge> = Vec::new();
        let mut grid: BTreeMap<(i64, i64, i64), usize> = BTreeMap::new();
        let tol = options.endpoint_tolerance.max(1e-9);

        let quantize = |p: Vec3| -> (i64, i64, i64) {
            (
                (p.x / tol).round() as i64,
                (p.y / tol).round() as i64,
                (p.z / tol).round() as i64,
            )
        };

        let sample_opts = SamplingOptions {
            tolerance: options.curve_tolerance,
            ..Default::default()
        };

        for occ in &scene.occurrences {
            for contour in &occ.geometry.contours {
                for curve in &contour.curves {
                    let pts = CurveSampler::sample(curve, &sample_opts);
                    if pts.len() < 2 {
                        continue;
                    }
                    let start = pts[0];
                    let end = *pts.last().unwrap();
                    let a = *grid.entry(quantize(start)).or_insert_with(|| {
                        let id = nodes.len();
                        nodes.push(GraphNode {
                            id,
                            position: start,
                            edges: Vec::new(),
                        });
                        id
                    });
                    let b = *grid.entry(quantize(end)).or_insert_with(|| {
                        let id = nodes.len();
                        nodes.push(GraphNode {
                            id,
                            position: end,
                            edges: Vec::new(),
                        });
                        id
                    });
                    let length = pts.windows(2).map(|w| Vec3::distance(w[0], w[1])).sum();
                    let eid = edges.len();
                    edges.push(GraphEdge {
                        id: eid,
                        a,
                        b,
                        length,
                        entity: occ.entity.clone(),
                        occurrence: occ.key.clone(),
                    });
                    nodes[a].edges.push(eid);
                    if b != a {
                        nodes[b].edges.push(eid);
                    }
                }
            }
        }

        TopologyGraph {
            document_id: scene.document_id.clone(),
            revision: scene.revision,
            nodes,
            edges,
        }
    }
}
