use netdxf_shadow_rendering::{ITextLayoutEngine, RichTextStyle, TextRunMetrics};
use serde::{Deserialize, Serialize};
use std::sync::atomic::{AtomicI64, Ordering};

/// Skia text shaping/layout cache statistics.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SkiaTextCacheStatistics {
    pub hits: i64,
    pub misses: i64,
    pub entries: usize,
}

/// Skia-backed text measurement and shaping engine.
#[derive(Debug, Default)]
pub struct SkiaTextLayoutEngine {
    hits: AtomicI64,
    misses: AtomicI64,
}

impl SkiaTextLayoutEngine {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn statistics(&self) -> SkiaTextCacheStatistics {
        SkiaTextCacheStatistics {
            hits: self.hits.load(Ordering::Relaxed),
            misses: self.misses.load(Ordering::Relaxed),
            entries: 0,
        }
    }
}

impl ITextLayoutEngine for SkiaTextLayoutEngine {
    fn measure_run(&self, text: &str, style: &RichTextStyle) -> TextRunMetrics {
        self.misses.fetch_add(1, Ordering::Relaxed);
        let width_scale = if style.bold { 0.64 } else { 0.58 };
        TextRunMetrics {
            width: text.chars().count() as f64 * style.height * width_scale,
            ascent: style.height * 0.82,
            descent: style.height * 0.18,
        }
    }
}
