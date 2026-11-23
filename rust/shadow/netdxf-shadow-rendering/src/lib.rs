//! Backend-agnostic CAD render compiler, SVG renderer, rich text, and camera (`netDxf.Shadow.Rendering`).

#![forbid(unsafe_code)]

pub mod render_core;
pub mod rich_text;

pub use render_core::{
    AnnotationScale, GradientPlan, ILinetypeDecorationProvider, IRasterRenderBackend,
    IRenderBackend, ISvgImageResolver, ITextOutlineProvider, LinetypeFrameMode,
    LinetypeMetricSpace, LinetypePlanOptions, LinetypePlacement, LinetypePlanner,
    LinetypeTextRotationPivot, LinetypeTransport, ModelAnnotationContext, ModelAnnotationMode,
    ModelAnnotationResolution, PaperSpaceOptions, PaperViewportProjection, PerspectiveCamera,
    PerspectiveFieldOfView, PerspectiveOptions, PickingRay, RenderClip, RenderCompileOptions,
    RenderCompiler, RenderCompilerStatistics, RenderContour, RenderFrame, RenderHitTesting,
    RenderHost, RenderImage, RenderItem, RenderPaint, RenderRegion, RenderScene, RenderStrokeGroup,
    StrokePattern, SvgImageData, SvgRenderBackend, Viewport, ViewportLayerOverride,
    ViewportLayerProfile, ViewportOverrideCatalog, ViewportOverrideLimits,
};
pub use rich_text::{
    ApproximateTextLayoutEngine, ITextLayoutEngine, MTextParagraphControl, ParagraphAlignment,
    ParagraphLineSpacing, ParagraphTabAlignment, ParagraphTabStop, PositionedTextRun, RichText,
    RichTextDocument, RichTextInline, RichTextLayout, RichTextLayoutResult, RichTextLine,
    RichTextParagraphStyle, RichTextParseOptions, RichTextStyle, TextDecoration,
    TextFormatDiagnostic, TextLayout, TextLine, TextRule, TextRunMetrics,
};
