#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DomainConnectivityOptions {
    pub tolerance: f64,
    pub maximum_nodes: usize,
}

impl Default for DomainConnectivityOptions {
    fn default() -> Self {
        Self {
            tolerance: 0.05,
            maximum_nodes: 50_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainPortNode {
    pub id: usize,
    pub entity: ObjectId,
    pub port_name: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DomainConnection {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub distance: f64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainConnectivityIssue {
    pub code: String,
    pub message: String,
    pub entity: Option<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainNetworkComponent {
    pub id: usize,
    pub nodes: Vec<usize>,
}

pub struct DomainConnectivity;

impl DomainConnectivity {
    /// Connects port nodes within `options.tolerance` and computes connected network components.
    pub fn analyze(
        ports: &[DomainPortNode],
        options: &DomainConnectivityOptions,
    ) -> (Vec<DomainConnection>, Vec<DomainNetworkComponent>) {
        let mut connections = Vec::new();
        let mut adj = vec![Vec::new(); ports.len()];
        let tol = options.tolerance.max(0.0);

        for i in 0..ports.len() {
            for j in (i + 1)..ports.len() {
                let d = Vec3::distance(ports[i].position, ports[j].position);
                if d <= tol {
                    let cid = connections.len();
                    connections.push(DomainConnection {
                        id: cid,
                        a: i,
                        b: j,
                        distance: d,
                    });
                    adj[i].push(j);
                    adj[j].push(i);
                }
            }
        }

        let mut visited = vec![false; ports.len()];
        let mut components = Vec::new();
        for start in 0..ports.len() {
            if visited[start] {
                continue;
            }
            let mut stack = vec![start];
            visited[start] = true;
            let mut nodes = Vec::new();
            while let Some(u) = stack.pop() {
                nodes.push(u);
                for &v in &adj[u] {
                    if !visited[v] {
                        visited[v] = true;
                        stack.push(v);
                    }
                }
            }
            nodes.sort_unstable();
            components.push(DomainNetworkComponent {
                id: components.len(),
                nodes,
            });
        }
        (connections, components)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationOptions {
    pub respect_closed_valves: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainIsolationExclusion {
    pub entity: ObjectId,
    pub reason: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationTraversalSummary {
    pub visited_nodes: usize,
    pub blocked_nodes: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationAssessment {
    pub isolated: bool,
    pub exclusions: Vec<DomainIsolationExclusion>,
    pub summary: DomainIsolationTraversalSummary,
}

pub struct DomainConnectivityIsolation;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainStructureOptions {
    pub maximum_loops: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainStrongComponent {
    pub id: usize,
    pub nodes: Vec<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainPortStructure {
    pub node: usize,
    pub component: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainCondensationLink {
    pub source_component: usize,
    pub target_component: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainCriticalConnection {
    pub connection_id: usize,
    pub is_bridge: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainLoopStep {
    pub node: usize,
    pub connection: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainLoop {
    pub steps: Vec<DomainLoopStep>,
}

pub struct DomainConnectivityStructure;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainPortVisit {
    pub node: usize,
    pub depth: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainConnectionProof {
    pub connected: bool,
    pub path: Vec<DomainPortVisit>,
}

pub struct DomainConnectivityWalk;

impl DomainConnectivityWalk {
    /// Proves connectivity between `start` and `goal` over `connections` via BFS.
    pub fn prove_connection(
        node_count: usize,
        connections: &[DomainConnection],
        start: usize,
        goal: usize,
    ) -> DomainConnectionProof {
        if start >= node_count || goal >= node_count {
            return DomainConnectionProof {
                connected: false,
                path: Vec::new(),
            };
        }
        let mut adj = vec![Vec::new(); node_count];
        for c in connections {
            if c.a < node_count && c.b < node_count {
                adj[c.a].push(c.b);
                adj[c.b].push(c.a);
            }
        }
        let mut visited = vec![false; node_count];
        let mut parent = vec![usize::MAX; node_count];
        let mut queue = std::collections::VecDeque::new();
        visited[start] = true;
        queue.push_back(start);

        while let Some(u) = queue.pop_front() {
            if u == goal {
                let mut chain = vec![goal];
                let mut cur = goal;
                while cur != start {
                    cur = parent[cur];
                    chain.push(cur);
                }
                chain.reverse();
                let path = chain
                    .into_iter()
                    .enumerate()
                    .map(|(depth, node)| DomainPortVisit { node, depth })
                    .collect();
                return DomainConnectionProof {
                    connected: true,
                    path,
                };
            }
            for &v in &adj[u] {
                if !visited[v] {
                    visited[v] = true;
                    parent[v] = u;
                    queue.push_back(v);
                }
            }
        }

        DomainConnectionProof {
            connected: false,
            path: Vec::new(),
        }
    }
}

impl DomainConnectivityIsolation {
    pub fn assess(
        node_count: usize,
        connections: &[DomainConnection],
        start: usize,
        blocked_nodes: &[usize],
        _options: &DomainIsolationOptions,
    ) -> DomainIsolationAssessment {
        let blocked: std::collections::BTreeSet<usize> = blocked_nodes.iter().copied().collect();
        let filtered: Vec<DomainConnection> = connections
            .iter()
            .filter(|c| !blocked.contains(&c.a) && !blocked.contains(&c.b))
            .cloned()
            .collect();
        let mut visited = 0usize;
        for target in 0..node_count {
            if DomainConnectivityWalk::prove_connection(node_count, &filtered, start, target)
                .connected
            {
                visited += 1;
            }
        }
        DomainIsolationAssessment {
            isolated: visited < node_count,
            exclusions: Vec::new(),
            summary: DomainIsolationTraversalSummary {
                visited_nodes: visited,
                blocked_nodes: blocked.len(),
            },
        }
    }
}

impl DomainConnectivityStructure {
    pub fn critical_connections(
        node_count: usize,
        connections: &[DomainConnection],
    ) -> Vec<DomainCriticalConnection> {
        let edges: Vec<netdxf_shadow_graph::IndexedGraphConnection> = connections
            .iter()
            .map(|c| netdxf_shadow_graph::IndexedGraphConnection { a: c.a, b: c.b })
            .collect();
        let ll = netdxf_shadow_graph::IndexedGraphAnalysis::low_links(node_count, &edges);
        ll.bridges
            .into_iter()
            .map(|id| DomainCriticalConnection {
                connection_id: id,
                is_bridge: true,
            })
            .collect()
    }
}

