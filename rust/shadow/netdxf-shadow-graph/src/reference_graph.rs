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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ReferenceGraphDirection {
    #[default]
    Outgoing,
    Incoming,
    Both,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ReferencePathStatus {
    #[default]
    Found,
    NotFound,
    LimitReached,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceGraphOptions {
    pub roots: Vec<ObjectId>,
    pub direction: ReferenceGraphDirection,
    pub max_depth: usize,
    pub maximum_depth: usize,
    pub max_nodes: usize,
    pub maximum_nodes: usize,
    pub maximum_edges: usize,
}

impl Default for ReferenceGraphOptions {
    fn default() -> Self {
        Self {
            roots: Vec::new(),
            direction: ReferenceGraphDirection::Outgoing,
            max_depth: 16,
            maximum_depth: 16,
            max_nodes: 10_000,
            maximum_nodes: 10_000,
            maximum_edges: 50_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ReferenceGraphNode {
    pub id: String,
    pub r#type: String,
    pub node_type: String,
    pub kind: String,
    pub name: Option<String>,
    pub root: bool,
    pub exists: bool,
    pub depth: usize,
    pub incoming: usize,
    pub outgoing: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceGraphEdge {
    pub source: String,
    pub path: String,
    pub target: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ReferenceGraph {
    pub roots: Vec<String>,
    pub direction: ReferenceGraphDirection,
    pub nodes: Vec<ReferenceGraphNode>,
    pub edges: Vec<ReferenceGraphEdge>,
    pub objects_indexed: usize,
    pub depth_limit_reached: bool,
    pub node_limit_reached: bool,
    pub edge_limit_reached: bool,
    pub truncated: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceComponent {
    pub id: usize,
    pub nodes: Vec<String>,
    pub internal_edge_count: usize,
    pub cyclic: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceCondensationEdge {
    pub source: usize,
    pub target: usize,
    pub reference_count: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceComponentAnalysis {
    pub scope: ReferenceGraph,
    pub components: Vec<ReferenceComponent>,
    pub condensation_edges: Vec<ReferenceCondensationEdge>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferencePathOptions {
    pub direction: ReferenceGraphDirection,
    pub max_depth: usize,
    pub max_visited: usize,
}

impl Default for ReferencePathOptions {
    fn default() -> Self {
        Self {
            direction: ReferenceGraphDirection::Outgoing,
            max_depth: 32,
            max_visited: 10_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ReferencePathResult {
    pub status: ReferencePathStatus,
    pub direction: ReferenceGraphDirection,
    pub nodes: Vec<String>,
    pub edges: Vec<ReferenceGraphEdge>,
    pub visited_nodes: usize,
    pub examined_edges: usize,
    pub depth_limit_reached: bool,
    pub node_limit_reached: bool,
    pub edge_limit_reached: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceImpactVisit {
    pub id: String,
    pub depth: usize,
    pub predecessor: Option<String>,
    pub via: Option<ReferenceGraphEdge>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceImpactResult {
    pub scope: ReferenceGraph,
    pub visits: Vec<ReferenceImpactVisit>,
    pub examined_edges: usize,
}

/// Indexed reference graph over a [`ShadowSnapshot`].
#[derive(Debug, Clone, Default)]
pub struct ReferenceGraphIndex {
    outgoing: BTreeMap<String, Vec<ReferenceGraphEdge>>,
    incoming: BTreeMap<String, Vec<ReferenceGraphEdge>>,
    nodes: BTreeMap<String, ReferenceGraphNode>,
}

impl ReferenceGraphIndex {
    pub fn build(snapshot: &ShadowSnapshot) -> Self {
        let mut idx = Self::default();
        for (id, obj) in &snapshot.objects {
            let id_str = id.as_str().to_string();
            idx.nodes.insert(
                id_str.clone(),
                ReferenceGraphNode {
                    id: id_str.clone(),
                    r#type: obj.short_type().to_string(),
                    node_type: obj.short_type().to_string(),
                    kind: format!("{:?}", obj.kind),
                    name: None,
                    exists: true,
                    ..Default::default()
                },
            );
            for (prop, val) in &obj.properties {
                for target in val.references() {
                    let edge = ReferenceGraphEdge {
                        source: id_str.clone(),
                        path: prop.clone(),
                        target: target.as_str().to_string(),
                    };
                    idx.outgoing
                        .entry(id_str.clone())
                        .or_default()
                        .push(edge.clone());
                    idx.incoming
                        .entry(target.as_str().to_string())
                        .or_default()
                        .push(edge);
                }
            }
        }
        idx
    }

    pub fn subgraph(&self, roots: &[ObjectId], options: &ReferenceGraphOptions) -> ReferenceGraph {
        let mut visited = BTreeSet::new();
        let mut queue = VecDeque::new();
        let mut out_nodes = Vec::new();
        let mut out_edges = Vec::new();

        for r in roots {
            let s = r.as_str().to_string();
            if visited.insert(s.clone()) {
                queue.push_back((s, 0usize));
            }
        }

        while let Some((cur, depth)) = queue.pop_front() {
            if out_nodes.len() >= options.max_nodes {
                break;
            }
            if let Some(node) = self.nodes.get(&cur) {
                out_nodes.push(node.clone());
            }
            if depth >= options.max_depth {
                continue;
            }
            let mut next_edges = Vec::new();
            if matches!(
                options.direction,
                ReferenceGraphDirection::Outgoing | ReferenceGraphDirection::Both
            ) {
                if let Some(es) = self.outgoing.get(&cur) {
                    next_edges.extend(es.iter().cloned());
                }
            }
            if matches!(
                options.direction,
                ReferenceGraphDirection::Incoming | ReferenceGraphDirection::Both
            ) {
                if let Some(es) = self.incoming.get(&cur) {
                    next_edges.extend(es.iter().cloned());
                }
            }
            for e in next_edges {
                let neighbor = if e.source == cur {
                    e.target.clone()
                } else {
                    e.source.clone()
                };
                out_edges.push(e);
                if visited.insert(neighbor.clone()) {
                    queue.push_back((neighbor, depth + 1));
                }
            }
        }

        ReferenceGraph {
            roots: roots.iter().map(|r| r.as_str().to_string()).collect(),
            direction: options.direction,
            nodes: out_nodes,
            edges: out_edges,
            objects_indexed: self.nodes.len(),
            ..Default::default()
        }
    }

    pub fn find_path(
        &self,
        start: &ObjectId,
        target: &ObjectId,
        options: &ReferencePathOptions,
    ) -> ReferencePathResult {
        let s = start.as_str().to_string();
        let t = target.as_str().to_string();
        if s == t {
            return ReferencePathResult {
                status: ReferencePathStatus::Found,
                direction: options.direction,
                nodes: vec![s],
                edges: Vec::new(),
                visited_nodes: 1,
                ..Default::default()
            };
        }
        let mut visited = BTreeSet::new();
        let mut prev: BTreeMap<String, (String, ReferenceGraphEdge)> = BTreeMap::new();
        let mut queue = VecDeque::new();
        visited.insert(s.clone());
        queue.push_back((s.clone(), 0usize));

        while let Some((cur, depth)) = queue.pop_front() {
            if visited.len() > options.max_visited {
                return ReferencePathResult {
                    status: ReferencePathStatus::LimitReached,
                    direction: options.direction,
                    nodes: Vec::new(),
                    edges: Vec::new(),
                    visited_nodes: visited.len(),
                    node_limit_reached: true,
                    ..Default::default()
                };
            }
            if depth >= options.max_depth {
                continue;
            }
            let mut edges = Vec::new();
            if matches!(
                options.direction,
                ReferenceGraphDirection::Outgoing | ReferenceGraphDirection::Both
            ) {
                if let Some(es) = self.outgoing.get(&cur) {
                    edges.extend(es.iter().cloned());
                }
            }
            if matches!(
                options.direction,
                ReferenceGraphDirection::Incoming | ReferenceGraphDirection::Both
            ) {
                if let Some(es) = self.incoming.get(&cur) {
                    edges.extend(es.iter().cloned());
                }
            }
            for e in edges {
                let next = if e.source == cur {
                    e.target.clone()
                } else {
                    e.source.clone()
                };
                if visited.insert(next.clone()) {
                    prev.insert(next.clone(), (cur.clone(), e));
                    if next == t {
                        let mut nodes = vec![t.clone()];
                        let mut path_edges = Vec::new();
                        let mut walk = t;
                        while let Some((p, pe)) = prev.get(&walk) {
                            nodes.push(p.clone());
                            path_edges.push(pe.clone());
                            walk = p.clone();
                        }
                        nodes.reverse();
                        path_edges.reverse();
                        return ReferencePathResult {
                            status: ReferencePathStatus::Found,
                            direction: options.direction,
                            nodes,
                            edges: path_edges,
                            visited_nodes: visited.len(),
                            ..Default::default()
                        };
                    }
                    queue.push_back((next, depth + 1));
                }
            }
        }

        ReferencePathResult {
            status: ReferencePathStatus::NotFound,
            direction: options.direction,
            nodes: Vec::new(),
            edges: Vec::new(),
            visited_nodes: visited.len(),
            ..Default::default()
        }
    }

    pub fn impact_analysis(
        &self,
        roots: &[ObjectId],
        options: &ReferenceGraphOptions,
    ) -> ReferenceImpactResult {
        let scope = self.subgraph(roots, options);
        let visits = scope
            .nodes
            .iter()
            .enumerate()
            .map(|(i, n)| ReferenceImpactVisit {
                id: n.id.clone(),
                depth: usize::from(i > 0),
                predecessor: None,
                via: None,
            })
            .collect();
        let examined_edges = scope.edges.len();
        ReferenceImpactResult {
            scope,
            visits,
            examined_edges,
        }
    }

    pub fn component_analysis(
        &self,
        roots: &[ObjectId],
        options: &ReferenceGraphOptions,
    ) -> ReferenceComponentAnalysis {
        let scope = self.subgraph(roots, options);
        let id_to_idx: BTreeMap<&str, usize> = scope
            .nodes
            .iter()
            .enumerate()
            .map(|(i, n)| (n.id.as_str(), i))
            .collect();
        let arcs: Vec<crate::indexed_graph_analysis::IndexedGraphArc> = scope
            .edges
            .iter()
            .filter_map(|e| {
                Some(crate::indexed_graph_analysis::IndexedGraphArc {
                    source: *id_to_idx.get(e.source.as_str())?,
                    target: *id_to_idx.get(e.target.as_str())?,
                })
            })
            .collect();
        let scc = crate::indexed_graph_analysis::IndexedGraphAnalysis::strong_components(
            scope.nodes.len(),
            &arcs,
        );
        let mut comp_nodes: Vec<Vec<String>> = vec![Vec::new(); scc.component_count];
        for (idx, &cid) in scc.membership.iter().enumerate() {
            comp_nodes[cid].push(scope.nodes[idx].id.clone());
        }
        let components = comp_nodes
            .into_iter()
            .enumerate()
            .map(|(id, nodes)| {
                let cyclic = nodes.len() > 1;
                ReferenceComponent {
                    id,
                    nodes,
                    internal_edge_count: 0,
                    cyclic,
                }
            })
            .collect();
        ReferenceComponentAnalysis {
            scope,
            components,
            condensation_edges: Vec::new(),
        }
    }

    pub fn explore(&self, roots: &[ObjectId], options: &ReferenceGraphOptions) -> ReferenceGraph {
        self.subgraph(roots, options)
    }

    pub fn impact(&self, roots: &[ObjectId], options: &ReferenceGraphOptions) -> ReferenceImpactResult {
        self.impact_analysis(roots, options)
    }

    pub fn shortest_path(
        &self,
        start: &ObjectId,
        target: &ObjectId,
        options: &ReferencePathOptions,
    ) -> ReferencePathResult {
        self.find_path(start, target, options)
    }

    pub fn strongly_connected_components(
        &self,
        roots: &[ObjectId],
        options: &ReferenceGraphOptions,
    ) -> ReferenceComponentAnalysis {
        self.component_analysis(roots, options)
    }
}

