use netdxf_shadow::{ObjectId, ShadowSnapshot};
use serde::{Deserialize, Serialize};
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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceGraphOptions {
    pub direction: ReferenceGraphDirection,
    pub max_depth: usize,
    pub max_nodes: usize,
}

impl Default for ReferenceGraphOptions {
    fn default() -> Self {
        Self {
            direction: ReferenceGraphDirection::Outgoing,
            max_depth: 16,
            max_nodes: 10_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferenceGraphNode {
    pub id: String,
    pub node_type: String,
    pub kind: String,
    pub name: Option<String>,
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReferencePathResult {
    pub status: ReferencePathStatus,
    pub direction: ReferenceGraphDirection,
    pub nodes: Vec<String>,
    pub edges: Vec<ReferenceGraphEdge>,
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
                    node_type: obj.short_type().to_string(),
                    kind: format!("{:?}", obj.kind),
                    name: None,
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
        }
    }
}
