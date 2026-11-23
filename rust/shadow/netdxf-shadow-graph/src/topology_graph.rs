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

    /// Dijkstra shortest path between `start` and `goal` weighted by edge length.
    pub fn shortest_path(&self, start: usize, goal: usize) -> Option<GraphPath> {
        let n = self.nodes.len();
        if start >= n || goal >= n {
            return None;
        }
        if start == goal {
            return Some(GraphPath {
                nodes: vec![start],
                edges: Vec::new(),
                length: 0.0,
            });
        }
        let mut dist = vec![f64::INFINITY; n];
        let mut prev: Vec<Option<(usize, usize)>> = vec![None; n];
        let mut visited = vec![false; n];
        dist[start] = 0.0;

        for _ in 0..n {
            let mut u = None;
            let mut best = f64::INFINITY;
            for i in 0..n {
                if !visited[i] && dist[i] < best {
                    best = dist[i];
                    u = Some(i);
                }
            }
            let Some(u) = u else { break };
            if u == goal {
                break;
            }
            visited[u] = true;
            for &ei in &self.nodes[u].edges {
                let e = &self.edges[ei];
                let v = if e.a == u { e.b } else { e.a };
                let alt = dist[u] + e.length.max(0.0);
                if alt < dist[v] {
                    dist[v] = alt;
                    prev[v] = Some((u, ei));
                }
            }
        }

        if !dist[goal].is_finite() {
            return None;
        }
        let mut nodes = vec![goal];
        let mut edges = Vec::new();
        let mut cur = goal;
        while let Some((p, ei)) = prev[cur] {
            nodes.push(p);
            edges.push(ei);
            cur = p;
        }
        nodes.reverse();
        edges.reverse();
        Some(GraphPath {
            nodes,
            edges,
            length: dist[goal],
        })
    }

    /// Computes a fundamental cycle basis from non-tree edges of the spanning forest.
    pub fn cycle_basis(&self) -> GraphCycleBasis {
        let forest = self.spanning_forest();
        let mut in_tree = vec![false; self.edges.len()];
        for &ei in &forest.edges {
            in_tree[ei] = true;
        }
        let mut cycles = Vec::new();
        for e in &self.edges {
            if !in_tree[e.id] {
                cycles.push(GraphCycle {
                    id: cycles.len(),
                    closing_edge: e.id,
                    start: e.a,
                    ancestor: e.b,
                    edge_count: 1,
                });
            }
        }
        GraphCycleBasis { cycles }
    }

    /// Produces a paginated [`GraphVisualization`] snapshot.
    pub fn visualize(&self, options: &GraphVisualizationOptions) -> GraphVisualization {
        let forest = self.spanning_forest();
        let cycle_rank = self.edges.len().saturating_sub(forest.edges.len());
        let node_items: Vec<GraphVisualizationNode> = self
            .nodes
            .iter()
            .skip(options.offset)
            .take(options.limit)
            .map(|n| GraphVisualizationNode {
                id: n.id,
                x: n.position.x,
                y: n.position.y,
                z: n.position.z,
                degree: n.edges.len(),
                component: 0,
            })
            .collect();
        let edge_items: Vec<GraphVisualizationEdge> = self
            .edges
            .iter()
            .skip(options.offset)
            .take(options.limit)
            .map(|e| GraphVisualizationEdge {
                id: e.id,
                a: e.a,
                b: e.b,
                start: self.nodes[e.a].position,
                end: self.nodes[e.b].position,
                length: e.length,
            })
            .collect();
        let next_node = (options.offset + node_items.len() < self.nodes.len())
            .then_some(options.offset + node_items.len());
        let next_edge = (options.offset + edge_items.len() < self.edges.len())
            .then_some(options.offset + edge_items.len());
        GraphVisualization {
            node_count: self.nodes.len(),
            edge_count: self.edges.len(),
            component_count: forest.component_count,
            cycle_rank,
            nodes: GraphVisualizationPage {
                total: self.nodes.len(),
                offset: options.offset,
                items: node_items,
                next_offset: next_node,
            },
            edges: GraphVisualizationPage {
                total: self.edges.len(),
                offset: options.offset,
                items: edge_items,
                next_offset: next_edge,
            },
        }
    }
}

