//! CAD geometry projection, curves, hatch patterns, and spatial indexing (`netDxf.Shadow.Geometry`).

#![forbid(unsafe_code)]

pub mod cad_scene;
pub mod curves;
pub mod hatch_and_index;

pub use cad_scene::{
    CadComponent, CadComponentStyle, CadContour, CadFillRule, CadGeometry, CadGradient, CadImage,
    CadLineSpacing, CadLinetype, CadLinetypeAlignment, CadLinetypeDecoration,
    CadLinetypeDecorationKind, CadLinetypeElement, CadLinetypeRotation, CadOccurrence, CadRegion,
    CadScene, CadSceneBuilder, CadStrokeGroup, CadStyle, CadStyleSource, CadText,
    CadTextProjection, CadTextSource, DrawingUnitConversion, ICacheableEntityProjector,
    IEntityProjector, ProjectionCacheOptions, ProjectionCacheStatistics, SceneBuildStatistics,
    SceneOptions, StandardEntityProjector,
};
pub use curves::{
    Curve3, CurveSampler, EllipseCurve, LineCurve, NurbsCurve, ReversedCurve, SamplingOptions,
    WidthBoundaryCurve,
};
pub use hatch_and_index::{
    CadHatchIslandStyle, CadPatternLine, DashInterval, DashPattern, HatchBoundaryInfo,
    HatchBoundaryTopology, HatchIslandOptions, HatchIslandResolver, HatchPatternGenerator,
    HatchPatternOptions, SpatialIndex,
};
