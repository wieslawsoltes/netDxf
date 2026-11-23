use thiserror::Error;

/// Unified error type for all `netdxf` operations.
#[derive(Debug, Error)]
pub enum DxfError {
    /// Underlying I/O error while reading or writing DXF data.
    #[error("I/O error: {0}")]
    Io(#[from] std::io::Error),

    /// DXF database version is not supported for the requested operation.
    #[error("DXF version '{version}' is not supported")]
    VersionNotSupported { version: String },

    /// Invalid or malformed DXF file structure or token.
    #[error("DXF parse error at line {line}: {message}")]
    Parse { line: usize, message: String },

    /// Unexpected end of file while reading DXF data.
    #[error("unexpected end of DXF stream")]
    UnexpectedEof,

    /// Invalid value or argument provided to an API.
    #[error("invalid value: {0}")]
    InvalidValue(String),

    /// Table object name is invalid or already exists.
    #[error("invalid table object name '{name}': {reason}")]
    InvalidName { name: String, reason: String },

    /// Referenced object or table entry was not found.
    #[error("referenced {kind} '{name}' not found")]
    NotFound { kind: &'static str, name: String },

    /// Attempted to remove a reserved or in-use table object.
    #[error("cannot remove {kind} '{name}': {reason}")]
    CannotRemove {
        kind: &'static str,
        name: String,
        reason: String,
    },

    /// Binary DXF sentinel mismatch or corrupt binary record.
    #[error("invalid binary DXF data: {0}")]
    InvalidBinary(String),

    /// Unsupported or invalid text encoding.
    #[error("encoding error: {0}")]
    Encoding(String),
}

/// Convenience result alias for `netdxf`.
pub type Result<T> = std::result::Result<T, DxfError>;
