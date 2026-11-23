use netdxf_shadow_rendering::{ITextLayoutEngine, RichTextStyle, TextRunMetrics};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::atomic::{AtomicI64, Ordering};
use std::sync::RwLock;

/// Skia text shaping/layout cache statistics.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SkiaTextCacheStatistics {
    pub hits: i64,
    pub misses: i64,
    pub entries: usize,
}

type RunCacheKey = (String, String, u64, bool, bool);

/// Skia-backed text measurement and shaping engine with thread-safe glyph metrics cache.
#[derive(Debug, Default)]
pub struct SkiaTextLayoutEngine {
    hits: AtomicI64,
    misses: AtomicI64,
    cache: RwLock<HashMap<RunCacheKey, TextRunMetrics>>,
}

impl SkiaTextLayoutEngine {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn statistics(&self) -> SkiaTextCacheStatistics {
        let entries = self.cache.read().map(|c| c.len()).unwrap_or(0);
        SkiaTextCacheStatistics {
            hits: self.hits.load(Ordering::Relaxed),
            misses: self.misses.load(Ordering::Relaxed),
            entries,
        }
    }

    pub fn clear_cache(&self) {
        if let Ok(mut c) = self.cache.write() {
            c.clear();
        }
        self.hits.store(0, Ordering::Relaxed);
        self.misses.store(0, Ordering::Relaxed);
    }
}

impl ITextLayoutEngine for SkiaTextLayoutEngine {
    fn measure_run(&self, text: &str, style: &RichTextStyle) -> TextRunMetrics {
        let key = (
            text.to_string(),
            style.font.clone(),
            style.height.to_bits(),
            style.bold,
            style.italic,
        );
        if let Ok(guard) = self.cache.read() {
            if let Some(&cached) = guard.get(&key) {
                self.hits.fetch_add(1, Ordering::Relaxed);
                return cached;
            }
        }
        self.misses.fetch_add(1, Ordering::Relaxed);
        let width_scale = if style.bold { 0.64 } else { 0.58 };
        let metrics = TextRunMetrics {
            width: text.chars().count() as f64 * style.height * width_scale,
            ascent: style.height * 0.82,
            descent: style.height * 0.18,
        };
        if let Ok(mut guard) = self.cache.write() {
            if guard.len() < 16_384 {
                guard.insert(key, metrics);
            }
        }
        metrics
    }
}
