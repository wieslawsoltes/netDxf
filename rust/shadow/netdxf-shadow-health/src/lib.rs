//! Drawing health diagnostics (`netDxf.Shadow.Health`).

#![forbid(unsafe_code)]

pub mod drawing_health;

pub use drawing_health::{
    DrawingHealth, DrawingHealthIssue, DrawingHealthOptions, DrawingHealthReport,
    DrawingHealthResources,
};
