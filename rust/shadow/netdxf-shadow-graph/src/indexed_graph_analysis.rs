use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct IndexedGraphArc {
    pub source: usize,
    pub target: usize,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct IndexedGraphConnection {
    pub a: usize,
    pub b: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct IndexedStrongComponents {
    pub membership: Vec<usize>,
    pub component_count: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct IndexedLowLinks {
    pub bridges: Vec<usize>,
    pub articulation_points: Vec<usize>,
}

/// Tarjan strongly connected components and bridge/articulation-point analysis on indexed graphs.
pub struct IndexedGraphAnalysis;

impl IndexedGraphAnalysis {
    /// Computes Tarjan strongly connected components for a directed graph with `node_count` vertices.
    pub fn strong_components(
        node_count: usize,
        arcs: &[IndexedGraphArc],
    ) -> IndexedStrongComponents {
        let mut adj = vec![Vec::new(); node_count];
        for arc in arcs {
            if arc.source < node_count && arc.target < node_count {
                adj[arc.source].push(arc.target);
            }
        }
        let mut index = 0usize;
        let mut indices = vec![usize::MAX; node_count];
        let mut lowlink = vec![0usize; node_count];
        let mut on_stack = vec![false; node_count];
        let mut stack = Vec::new();
        let mut membership = vec![0usize; node_count];
        let mut component_count = 0usize;

        #[allow(clippy::too_many_arguments)]
        fn dfs(
            v: usize,
            adj: &[Vec<usize>],
            index: &mut usize,
            indices: &mut [usize],
            lowlink: &mut [usize],
            on_stack: &mut [bool],
            stack: &mut Vec<usize>,
            membership: &mut [usize],
            component_count: &mut usize,
        ) {
            indices[v] = *index;
            lowlink[v] = *index;
            *index += 1;
            stack.push(v);
            on_stack[v] = true;

            for &w in &adj[v] {
                if indices[w] == usize::MAX {
                    dfs(
                        w,
                        adj,
                        index,
                        indices,
                        lowlink,
                        on_stack,
                        stack,
                        membership,
                        component_count,
                    );
                    lowlink[v] = lowlink[v].min(lowlink[w]);
                } else if on_stack[w] {
                    lowlink[v] = lowlink[v].min(indices[w]);
                }
            }

            if lowlink[v] == indices[v] {
                let cid = *component_count;
                *component_count += 1;
                while let Some(w) = stack.pop() {
                    on_stack[w] = false;
                    membership[w] = cid;
                    if w == v {
                        break;
                    }
                }
            }
        }

        for v in 0..node_count {
            if indices[v] == usize::MAX {
                dfs(
                    v,
                    &adj,
                    &mut index,
                    &mut indices,
                    &mut lowlink,
                    &mut on_stack,
                    &mut stack,
                    &mut membership,
                    &mut component_count,
                );
            }
        }

        IndexedStrongComponents {
            membership,
            component_count,
        }
    }

    /// Computes bridges and articulation points for an undirected graph.
    pub fn low_links(node_count: usize, edges: &[IndexedGraphConnection]) -> IndexedLowLinks {
        let mut adj = vec![Vec::new(); node_count];
        for (ei, e) in edges.iter().enumerate() {
            if e.a < node_count && e.b < node_count {
                adj[e.a].push((e.b, ei));
                adj[e.b].push((e.a, ei));
            }
        }
        let mut timer = 0usize;
        let mut disc = vec![usize::MAX; node_count];
        let mut low = vec![0usize; node_count];
        let mut is_art = vec![false; node_count];
        let mut bridges = Vec::new();

        #[allow(clippy::too_many_arguments)]
        fn dfs(
            u: usize,
            parent_edge: Option<usize>,
            adj: &[Vec<(usize, usize)>],
            timer: &mut usize,
            disc: &mut [usize],
            low: &mut [usize],
            is_art: &mut [bool],
            bridges: &mut Vec<usize>,
        ) {
            disc[u] = *timer;
            low[u] = *timer;
            *timer += 1;
            let mut children = 0usize;

            for &(v, ei) in &adj[u] {
                if Some(ei) == parent_edge {
                    continue;
                }
                if disc[v] == usize::MAX {
                    children += 1;
                    dfs(v, Some(ei), adj, timer, disc, low, is_art, bridges);
                    low[u] = low[u].min(low[v]);
                    if low[v] > disc[u] {
                        bridges.push(ei);
                    }
                    if parent_edge.is_some() && low[v] >= disc[u] {
                        is_art[u] = true;
                    }
                } else {
                    low[u] = low[u].min(disc[v]);
                }
            }
            if parent_edge.is_none() && children > 1 {
                is_art[u] = true;
            }
        }

        for u in 0..node_count {
            if disc[u] == usize::MAX {
                dfs(
                    u,
                    None,
                    &adj,
                    &mut timer,
                    &mut disc,
                    &mut low,
                    &mut is_art,
                    &mut bridges,
                );
            }
        }

        bridges.sort_unstable();
        let articulation_points = is_art
            .into_iter()
            .enumerate()
            .filter_map(|(i, flag)| flag.then_some(i))
            .collect();

        IndexedLowLinks {
            bridges,
            articulation_points,
        }
    }
}
