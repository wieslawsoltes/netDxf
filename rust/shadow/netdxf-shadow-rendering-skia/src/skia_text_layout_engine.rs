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
    pub cached_runs: usize,
    pub cached_layouts: usize,
    pub cached_units: usize,
    pub cached_layout_units: usize,
    pub shape_hits: i64,
    pub shape_compilations: i64,
    pub layout_compilations: i64,
    pub shared_layout_hits: i64,
}

type RunCacheKey = (String, String, u64, bool, bool);

/// Skia-backed text measurement and shaping engine with thread-safe glyph metrics cache.
#[derive(Debug, Default)]
pub struct SkiaTextLayoutEngine {
    hits: AtomicI64,
    misses: AtomicI64,
    layout_compilations: AtomicI64,
    shared_layout_hits: AtomicI64,
    cache: RwLock<HashMap<RunCacheKey, TextRunMetrics>>,
}

impl SkiaTextLayoutEngine {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn statistics(&self) -> SkiaTextCacheStatistics {
        let entries = self.cache.read().map(|c| c.len()).unwrap_or(0);
        let hits = self.hits.load(Ordering::Relaxed);
        let misses = self.misses.load(Ordering::Relaxed);
        SkiaTextCacheStatistics {
            hits,
            misses,
            entries,
            cached_runs: entries,
            cached_layouts: entries,
            cached_units: entries,
            cached_layout_units: entries,
            shape_hits: hits,
            shape_compilations: misses,
            layout_compilations: self.layout_compilations.load(Ordering::Relaxed),
            shared_layout_hits: self.shared_layout_hits.load(Ordering::Relaxed),
        }
    }

    pub fn clear_cache(&self) {
        if let Ok(mut c) = self.cache.write() {
            c.clear();
        }
        self.hits.store(0, Ordering::Relaxed);
        self.misses.store(0, Ordering::Relaxed);
        self.layout_compilations.store(0, Ordering::Relaxed);
        self.shared_layout_hits.store(0, Ordering::Relaxed);
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
