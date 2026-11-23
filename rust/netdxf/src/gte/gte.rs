/// Global Geometric Tools Engine configuration flags.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GTE;

impl GTE {
    /// Whether 2D matrices use row-major storage order (default: `true`).
    pub const USE_ROW_MAJOR: bool = true;
}
