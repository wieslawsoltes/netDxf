//! Interactive CAD editor session, undo/redo history, snapping, and edit operations (`netDxf.Shadow.Editor`).

#![forbid(unsafe_code)]

pub mod affine_edit_operations;
pub mod custom_header_edits;
pub mod dimension_translation;
pub mod edit_operations;
pub mod editor_session;
pub mod selection_translation;
pub mod snapping;

pub use affine_edit_operations::*;
pub use custom_header_edits::*;
pub use dimension_translation::*;
pub use edit_operations::*;
pub use editor_session::*;
pub use selection_translation::*;
pub use snapping::*;
