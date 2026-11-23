use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowCaptureOptions {
    pub maximum_objects: usize,
    pub maximum_depth: usize,
    pub maximum_value_depth: usize,
    pub maximum_collection_items: usize,
    pub fail_on_unreadable_property: bool,
    pub include_xdata: bool,
}

impl Default for ShadowCaptureOptions {
    fn default() -> Self {
        Self {
            maximum_objects: 1_000_000,
            maximum_depth: 64,
            maximum_value_depth: 64,
            maximum_collection_items: 250_000,
            fail_on_unreadable_property: false,
            include_xdata: true,
        }
    }
}
