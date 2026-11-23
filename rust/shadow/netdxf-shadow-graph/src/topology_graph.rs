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
                dead_end: n.edges.len() == 1,
                isolated: n.edges.is_empty(),
                junction: n.edges.len() >= 3,
                ..Default::default()
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
                entity: Some(e.entity.clone()),
                root_entity: Some(e.entity.clone()),
                occurrence: e.occurrence.clone(),
                ..Default::default()
            })
            .collect();
        let next_node = (options.offset + node_items.len() < self.nodes.len())
            .then_some(options.offset + node_items.len());
        let next_edge = (options.offset + edge_items.len() < self.edges.len())
            .then_some(options.offset + edge_items.len());
        let (art_pts, bridges) = self.tarjan_articulation_and_bridges();
        GraphVisualization {
            node_count: self.nodes.len(),
            edge_count: self.edges.len(),
            component_count: forest.component_count,
            cycle_rank,
            articulation_point_count: art_pts.len(),
            bridge_count: bridges.len(),
            dead_end_count: self.dead_ends().len(),
            isolated_node_count: self.isolated().len(),
            junction_count: self.junctions().len(),
            diagnostic_count: self.diagnostics().len(),
            total_length: self.length(),
            node_limit: options.limit,
            edge_limit: options.limit,
            truncated: next_node.is_some() || next_edge.is_some(),
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

    /// Total length of all edges in the topology graph.
    pub fn length(&self) -> f64 {
        self.edges.iter().map(|e| e.length).sum()
    }

    /// Cyclomatic number `E - V + C`.
    pub fn cycle_rank(&self) -> usize {
        let forest = self.spanning_forest();
        self.edges
            .len()
            .saturating_sub(self.nodes.len())
            .saturating_add(forest.component_count)
    }

    /// Returns connected components as lists of node IDs.
    pub fn connected_components(&self) -> Vec<Vec<usize>> {
        let n = self.nodes.len();
        let mut visited = vec![false; n];
        let mut comps = Vec::new();
        for start in 0..n {
            if visited[start] {
                continue;
            }
            let mut comp = Vec::new();
            let mut q = VecDeque::new();
            visited[start] = true;
            q.push_back(start);
            while let Some(u) = q.pop_front() {
                comp.push(u);
                for &ei in &self.nodes[u].edges {
                    let e = &self.edges[ei];
                    let v = if e.a == u { e.b } else { e.a };
                    if !visited[v] {
                        visited[v] = true;
                        q.push_back(v);
                    }
                }
            }
            comps.push(comp);
        }
        comps
    }

    /// Alias for `connected_components`.
    pub fn components(&self) -> Vec<Vec<usize>> {
        self.connected_components()
    }

    /// Nodes with degree 0.
    pub fn isolated(&self) -> Vec<usize> {
        self.nodes
            .iter()
            .filter(|n| n.edges.is_empty())
            .map(|n| n.id)
            .collect()
    }

    /// Nodes with degree 1.
    pub fn dead_ends(&self) -> Vec<usize> {
        self.nodes
            .iter()
            .filter(|n| n.edges.len() == 1)
            .map(|n| n.id)
            .collect()
    }

    /// Nodes with degree >= 3.
    pub fn junctions(&self) -> Vec<usize> {
        self.nodes
            .iter()
            .filter(|n| n.edges.len() >= 3)
            .map(|n| n.id)
            .collect()
    }

    /// Computes articulation points (cut vertices) and bridges (cut edges) via Tarjan's DFS algorithm.
    fn tarjan_articulation_and_bridges(&self) -> (Vec<usize>, Vec<usize>) {
        let n = self.nodes.len();
        let mut disc = vec![0usize; n];
        let mut low = vec![0usize; n];
        let mut timer = 0usize;
        let mut is_art = vec![false; n];
        let mut bridges = Vec::new();

        #[allow(clippy::too_many_arguments)]
        fn dfs(
            g: &TopologyGraph,
            u: usize,
            parent_edge: Option<usize>,
            timer: &mut usize,
            disc: &mut [usize],
            low: &mut [usize],
            is_art: &mut [bool],
            bridges: &mut Vec<usize>,
        ) {
            *timer += 1;
            disc[u] = *timer;
            low[u] = *timer;
            let mut children = 0usize;
            for &ei in &g.nodes[u].edges {
                if Some(ei) == parent_edge {
                    continue;
                }
                let e = &g.edges[ei];
                let v = if e.a == u { e.b } else { e.a };
                if disc[v] == 0 {
                    children += 1;
                    dfs(g, v, Some(ei), timer, disc, low, is_art, bridges);
                    low[u] = low[u].min(low[v]);
                    if parent_edge.is_some() && low[v] >= disc[u] {
                        is_art[u] = true;
                    }
                    if low[v] > disc[u] {
                        bridges.push(ei);
                    }
                } else {
                    low[u] = low[u].min(disc[v]);
                }
            }
            if parent_edge.is_none() && children > 1 {
                is_art[u] = true;
            }
        }

        for u in 0..n {
            if disc[u] == 0 {
                dfs(self, u, None, &mut timer, &mut disc, &mut low, &mut is_art, &mut bridges);
            }
        }
        let art_pts = is_art
            .into_iter()
            .enumerate()
            .filter_map(|(i, b)| b.then_some(i))
            .collect();
        bridges.sort_unstable();
        (art_pts, bridges)
    }

    /// Articulation points (cut vertices) of the graph.
    pub fn articulation_points(&self) -> Vec<usize> {
        self.tarjan_articulation_and_bridges().0
    }

    /// Bridges (cut edges) of the graph.
    pub fn bridges(&self) -> Vec<usize> {
        self.tarjan_articulation_and_bridges().1
    }

    /// Topological diagnostics (isolated nodes, self-loops, zero-length edges).
    pub fn diagnostics(&self) -> Vec<String> {
        let mut out = Vec::new();
        for n in self.isolated() {
            out.push(format!("isolated_node:{n}"));
        }
        for e in &self.edges {
            if e.a == e.b {
                out.push(format!("self_loop_edge:{}", e.id));
            }
            if e.length <= 1e-12 {
                out.push(format!("zero_length_edge:{}", e.id));
            }
        }
        out
    }
}

