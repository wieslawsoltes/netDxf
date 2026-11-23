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
    pub connection_tolerance: f64,
    pub maximum_nodes: usize,
    pub maximum_edges: usize,
    pub maximum_issues: usize,
    pub maximum_objects: usize,
}

impl Default for DomainConnectivityOptions {
    fn default() -> Self {
        Self {
            tolerance: 0.05,
            connection_tolerance: 0.05,
            maximum_nodes: 50_000,
            maximum_edges: 100_000,
            maximum_issues: 1_000,
            maximum_objects: 100_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainPortNode {
    pub id: usize,
    pub key: String,
    pub entity: ObjectId,
    pub port_name: String,
    pub port: String,
    pub kind: String,
    pub class: String,
    pub module: Option<String>,
    pub nominal_size: Option<String>,
    pub position: Vec3,
    pub degree: usize,
    pub component: usize,
    pub open: bool,
    pub passages_declared: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainConnection {
    pub id: usize,
    pub a: usize,
    pub b: usize,
    pub source: usize,
    pub target: usize,
    pub distance: f64,
    pub span: f64,
    pub kind: String,
    pub direction: String,
    pub reciprocal: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainConnectivityIssue {
    pub code: String,
    pub message: String,
    pub entity: Option<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainNetworkComponent {
    pub id: usize,
    pub nodes: Vec<usize>,
    pub edge_count: usize,
    pub entity_count: usize,
    pub node_count: usize,
    pub open_port_count: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainConnectivity {
    pub document_id: String,
    pub module: String,
    pub layout: String,
    pub record_count: usize,
    pub objects_visited: usize,
    pub nodes: Vec<DomainPortNode>,
    pub edges: Vec<DomainConnection>,
    pub components: Vec<DomainNetworkComponent>,
    pub issues: Vec<DomainConnectivityIssue>,
    pub limitations: Vec<String>,
    pub complete: bool,
    pub declarations_complete: bool,
    pub issues_complete: bool,
}

impl DomainConnectivity {
    pub const MAXIMUM_CONNECTION_ID_LENGTH: usize = 256;
    pub const MAXIMUM_ISOLATION_EXCLUSION_CHARACTERS: usize = 16_384;
    pub const MAXIMUM_ISOLATION_EXCLUSIONS: usize = 256;
    pub const MAXIMUM_PORT_ID_LENGTH: usize = 256;

    pub fn port_id(entity: &ObjectId, port_name: &str) -> String {
        format!("{}:{port_name}", entity.as_str())
    }
    pub fn build(
        document_id: impl Into<String>,
        mut ports: Vec<DomainPortNode>,
        options: &DomainConnectivityOptions,
    ) -> Self {
        let (edges, components) = Self::analyze(&ports, options);
        for (comp_idx, comp) in components.iter().enumerate() {
            for &ni in &comp.nodes {
                if let Some(p) = ports.get_mut(ni) {
                    p.component = comp_idx;
                }
            }
        }
        for e in &edges {
            if let Some(p) = ports.get_mut(e.a) {
                p.degree += 1;
            }
            if let Some(p) = ports.get_mut(e.b) {
                p.degree += 1;
            }
        }
        for p in &mut ports {
            p.open = p.degree == 0;
        }
        Self {
            document_id: document_id.into(),
            layout: "Model".into(),
            record_count: ports.len(),
            objects_visited: ports.len(),
            nodes: ports,
            edges,
            components,
            issues: Vec::new(),
            complete: true,
            declarations_complete: true,
            issues_complete: true,
            ..Default::default()
        }
    }

    pub fn analyze_structure(&self) -> Vec<DomainNetworkComponent> {
        self.components.clone()
    }

    pub fn analyze_isolation(&self, excluded_nodes: &[usize]) -> Vec<Vec<usize>> {
        let n = self.nodes.len();
        let mut adj = vec![Vec::new(); n];
        for e in &self.edges {
            if !excluded_nodes.contains(&e.a) && !excluded_nodes.contains(&e.b) {
                adj[e.a].push(e.b);
                adj[e.b].push(e.a);
            }
        }
        let mut visited = vec![false; n];
        let mut comps = Vec::new();
        for start in 0..n {
            if visited[start] || excluded_nodes.contains(&start) {
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
            comps.push(nodes);
        }
        comps
    }

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
                        source: i,
                        target: j,
                        distance: d,
                        span: d,
                        kind: "spatial".to_string(),
                        direction: "bidirectional".to_string(),
                        reciprocal: true,
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
            let node_count = nodes.len();
            components.push(DomainNetworkComponent {
                id: components.len(),
                nodes,
                node_count,
                ..Default::default()
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
    pub visited_count: usize,
    pub blocked_nodes: usize,
    pub examined_edges: usize,
    pub complete: bool,
    pub scope_complete: bool,
    pub depth_limit_reached: bool,
    pub edge_limit_reached: bool,
    pub visit_limit_reached: bool,
    pub target_status: Option<String>,
    pub target_path_length: Option<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainIsolationAssessment {
    pub id: usize,
    pub status: String,
    pub isolated: bool,
    pub before: DomainIsolationTraversalSummary,
    pub after: DomainIsolationTraversalSummary,
    pub exclusions: Vec<DomainIsolationExclusion>,
    pub summary: DomainIsolationTraversalSummary,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainConnectivityIsolation {
    pub direction: String,
    pub options: DomainIsolationOptions,
    pub exclusions: Vec<DomainIsolationExclusion>,
    pub before: DomainIsolationTraversalSummary,
    pub after: DomainIsolationTraversalSummary,
    pub before_visits: Vec<DomainPortVisit>,
    pub after_visits: Vec<DomainPortVisit>,
    pub assessments: Vec<DomainIsolationAssessment>,
    pub proof_to: Option<usize>,
    pub scope_complete: bool,
}

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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainCondensationLink {
    pub id: usize,
    pub source: usize,
    pub target: usize,
    pub connection_count: usize,
    pub source_component: usize,
    pub target_component: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainCriticalConnection {
    pub connection_id: usize,
    pub is_bridge: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainLoopStep {
    pub id: usize,
    pub step: usize,
    pub r#loop: usize,
    pub node: usize,
    pub source: usize,
    pub target: usize,
    pub connection: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainLoop {
    pub steps: Vec<DomainLoopStep>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainConnectivityStructure {
    pub options: DomainStructureOptions,
    pub port_structures: Vec<DomainPortStructure>,
    pub condensation: Vec<DomainCondensationLink>,
    pub loops: Vec<DomainLoop>,
    pub loop_steps: usize,
    pub loop_search_complete: bool,
    pub loop_limit_reached: bool,
    pub loop_proof: Option<DomainConnectionProof>,
    pub proof_limit_reached: bool,
    pub scope_complete: bool,
}

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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainConnectivityWalk {
    pub source: usize,
    pub target: Option<usize>,
    pub direction: String,
    pub status: String,
    pub complete: bool,
    pub scope_complete: bool,
    pub depth_limit_reached: bool,
    pub examined_edges: usize,
    pub path: Vec<DomainPortVisit>,
    pub proof_to: Option<DomainConnectionProof>,
}

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
        let summary = DomainIsolationTraversalSummary {
            visited_nodes: visited,
            visited_count: visited,
            blocked_nodes: blocked.len(),
            examined_edges: filtered.len(),
            complete: true,
            scope_complete: true,
            ..Default::default()
        };
        let isolated = visited < node_count;
        DomainIsolationAssessment {
            id: start,
            status: if isolated { "isolated".to_string() } else { "connected".to_string() },
            isolated,
            before: summary.clone(),
            after: summary.clone(),
            exclusions: Vec::new(),
            summary,
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

