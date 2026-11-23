use crate::TopologyGraph;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GraphSpanningForest {
    pub edges: Vec<usize>,
    pub component_count: usize,
    pub length: f64,
}

impl TopologyGraph {
    /// Kruskal minimum spanning forest weighted by sampled length. Isolated nodes remain components.
    /// Equal weights are ordered by edge ID, parallel edges compete normally and self loops are excluded.
    pub fn minimum_spanning_forest(&self) -> GraphSpanningForest {
        let n = self.nodes.len();
        let mut parents: Vec<usize> = (0..n).collect();
        let mut ranks = vec![0u8; n];
        let mut chosen = Vec::new();
        let mut components = n;
        let mut length = 0.0;

        fn find_root(parents: &mut [usize], mut node: usize) -> usize {
            while parents[node] != node {
                parents[node] = parents[parents[node]];
                node = parents[node];
            }
            node
        }

        let mut sorted_edges: Vec<&crate::GraphEdge> = self
            .edges
            .iter()
            .filter(|e| e.length.is_finite() && e.length >= 0.0)
            .collect();
        sorted_edges.sort_by(|a, b| {
            a.length
                .total_cmp(&b.length)
                .then_with(|| a.id.cmp(&b.id))
        });

        for edge in sorted_edges {
            if edge.a >= n || edge.b >= n {
                continue;
            }
            let mut a = find_root(&mut parents, edge.a);
            let mut b = find_root(&mut parents, edge.b);
            if a == b {
                continue;
            }
            if ranks[a] < ranks[b] {
                std::mem::swap(&mut a, &mut b);
            }
            parents[b] = a;
            if ranks[a] == ranks[b] {
                ranks[a] = ranks[a].saturating_add(1);
            }
            chosen.push(edge.id);
            components = components.saturating_sub(1);
            length += edge.length;
        }

        GraphSpanningForest {
            edges: chosen,
            component_count: components,
            length,
        }
    }
}
