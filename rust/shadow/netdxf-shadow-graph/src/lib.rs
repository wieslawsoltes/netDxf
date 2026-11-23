//! Topology and reference graph analysis (`netDxf.Shadow.Graph`).

#![forbid(unsafe_code)]

pub mod indexed_graph_analysis;
pub mod reference_graph;
pub mod topology_graph;

pub use indexed_graph_analysis::{
    IndexedGraphAnalysis, IndexedGraphArc, IndexedGraphConnection, IndexedLowLinks,
    IndexedStrongComponents,
};
pub use reference_graph::{
    ReferenceComponent, ReferenceComponentAnalysis, ReferenceCondensationEdge, ReferenceGraph,
    ReferenceGraphDirection, ReferenceGraphEdge, ReferenceGraphIndex, ReferenceGraphNode,
    ReferenceGraphOptions, ReferenceImpactResult, ReferenceImpactVisit, ReferencePathOptions,
    ReferencePathResult, ReferencePathStatus,
};
pub use topology_graph::{
    GraphCycle, GraphCycleBasis, GraphEdge, GraphNode, GraphPath, GraphSpanningForest,
    GraphTraversal, GraphVisit, GraphVisualization, GraphVisualizationEdge, GraphVisualizationNode,
    GraphVisualizationOptions, GraphVisualizationPage, TopologyBuilder, TopologyGraph,
    TopologyOptions,
};
