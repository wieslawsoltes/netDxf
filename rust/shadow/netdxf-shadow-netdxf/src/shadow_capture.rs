use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowCaptureOptions {
    pub maximum_objects: usize,
    pub maximum_depth: usize,
    pub include_xdata: bool,
}

impl Default for ShadowCaptureOptions {
    fn default() -> Self {
        Self {
            maximum_objects: 1_000_000,
            maximum_depth: 64,
            include_xdata: true,
        }
    }
}
