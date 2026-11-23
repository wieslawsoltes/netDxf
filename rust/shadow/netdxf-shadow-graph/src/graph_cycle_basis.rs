use crate::{GraphEdge, GraphPath, TopologyGraph};
use serde::{Deserialize, Serialize};

/// A fundamental cycle closed by one non-tree edge. IDs are local to the immutable graph.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct GraphCycle {
    pub id: usize,
    pub closing_edge: usize,
    pub start: usize,
    pub ancestor: usize,
    pub edge_count: usize,
}

/// Linear-space index of an independent cycle basis, including self loops and parallel edges.
/// Building the index does not materialize every path; expand only the cycles required by the caller.
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct GraphCycleBasis {
    pub cycles: Vec<GraphCycle>,
    #[serde(default)]
    pub(crate) parent_edges: Vec<Option<usize>>,
    #[serde(default)]
    pub(crate) graph_edges: Vec<GraphEdge>,
}

impl GraphCycleBasis {
    pub(crate) fn new(
        graph_edges: Vec<GraphEdge>,
        parent_edges: Vec<Option<usize>>,
        cycles: Vec<GraphCycle>,
    ) -> Self {
        Self {
            cycles,
            parent_edges,
            graph_edges,
        }
    }

    /// Expands one ordered closed path, including its repeated final node. No recursion is used.
    pub fn path(&self, cycle: usize) -> Option<GraphPath> {
        let descriptor = *self.cycles.get(cycle)?;
        let mut nodes = Vec::with_capacity(descriptor.edge_count + 1);
        let mut edges = Vec::with_capacity(descriptor.edge_count);
        let mut node = descriptor.start;
        let mut length = 0.0;
        nodes.push(node);

        while node != descriptor.ancestor {
            let parent_edge_id = self.parent_edges.get(node).copied().flatten()?;
            let edge = self.graph_edges.get(parent_edge_id)?;
            edges.push(edge.id);
            length += edge.length;
            node = if edge.a == node { edge.b } else { edge.a };
            nodes.push(node);
        }

        let closing = self.graph_edges.get(descriptor.closing_edge)?;
        edges.push(closing.id);
        length += closing.length;
        nodes.push(descriptor.start);

        Some(GraphPath {
            nodes,
            edges,
            length,
        })
    }
}

impl TopologyGraph {
    /// Deterministic depth-first fundamental cycle basis in O(nodes + edges) time and memory.
    /// Each non-tree edge closes a path to a DFS ancestor; cycles are ordered by closing edge ID.
    pub fn cycle_basis(&self) -> GraphCycleBasis {
        let n = self.nodes.len();
        let m = self.edges.len();
        let mut seen = vec![false; n];
        let mut edge_seen = vec![false; m];
        let mut parent: Vec<Option<usize>> = vec![None; n];
        let mut depth = vec![0usize; n];
        let mut next = vec![0usize; n];
        let mut stack: Vec<usize> = Vec::new();
        let mut closing: Vec<Option<GraphCycle>> = vec![None; m];

        for root in 0..n {
            if seen[root] {
                continue;
            }
            seen[root] = true;
            stack.push(root);
            while let Some(&node) = stack.last() {
                if next[node] == self.nodes[node].edges.len() {
                    stack.pop();
                    continue;
                }
                let edge_id = self.nodes[node].edges[next[node]];
                next[node] += 1;
                if edge_id >= m || edge_seen[edge_id] {
                    continue;
                }
                edge_seen[edge_id] = true;
                let edge = &self.edges[edge_id];
                let other = if edge.a == node { edge.b } else { edge.a };
                if !seen[other] {
                    seen[other] = true;
                    parent[other] = Some(edge_id);
                    depth[other] = depth[node] + 1;
                    stack.push(other);
                } else {
                    let edge_count = depth[node].saturating_sub(depth[other]) + 1;
                    closing[edge_id] = Some(GraphCycle {
                        id: 0,
                        closing_edge: edge_id,
                        start: node,
                        ancestor: other,
                        edge_count,
                    });
                }
            }
        }

        let mut cycles = Vec::new();
        for mut cycle in closing.into_iter().flatten() {
            cycle.id = cycles.len();
            cycles.push(cycle);
        }

        GraphCycleBasis::new(self.edges.clone(), parent, cycles)
    }
}
