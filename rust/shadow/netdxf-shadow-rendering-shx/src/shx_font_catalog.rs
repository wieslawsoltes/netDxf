#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadContour, CadGeometry, ICacheableEntityProjector, IEntityProjector};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShxBigFontBinding {
    pub font_name: String,
    pub code_page: String,
}

impl ShxBigFontBinding {
    pub fn font(&self) -> &str {
        &self.font_name
    }

    pub fn map(&self) -> &str {
        &self.code_page
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShxFontSet {
    pub revision: i64,
    pub fonts: BTreeMap<String, ShxFont>,
    pub big_font_maps: BTreeMap<String, ShxBigFontMap>,
}

impl ShxFontSet {
    pub fn file_name(name: &str) -> String {
        let trimmed = name.trim();
        if trimmed.to_ascii_lowercase().ends_with(".shx") {
            trimmed.to_string()
        } else {
            format!("{trimmed}.shx")
        }
    }

    pub fn resolve(&self, name: &str) -> Option<&ShxFont> {
        let fname = Self::file_name(name);
        self.fonts.get(name).or_else(|| self.fonts.get(&fname))
    }

    pub fn resolve_big_font(&self, name: &str) -> Option<&ShxBigFontMap> {
        let fname = Self::file_name(name);
        self.big_font_maps
            .get(name)
            .or_else(|| self.big_font_maps.get(&fname))
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShxFontCatalog {
    pub set: ShxFontSet,
    pub big_font_bindings: Vec<ShxBigFontBinding>,
}

impl ShxFontCatalog {
    #[inline]
    pub fn current(&self) -> &ShxFontSet {
        &self.set
    }

    pub fn register(&mut self, name: impl Into<String>, font: ShxFont) {
        self.set.fonts.insert(name.into(), font);
        self.set.revision += 1;
    }

    pub fn register_big_font(&mut self, name: impl Into<String>, map: ShxBigFontMap) {
        self.set.big_font_maps.insert(name.into(), map);
        self.set.revision += 1;
    }

    pub fn import_big_font_map(&mut self, name: impl Into<String>, content: &str) {
        let map = ShxBigFontMapFile::read(content, &ShxBigFontMapReadOptions::default());
        self.register_big_font(name, map);
    }

    pub fn remove(&mut self, name: &str) -> bool {
        let removed = self.set.fonts.remove(name).is_some()
            || self.set.big_font_maps.remove(name).is_some();
        if removed {
            self.set.revision += 1;
        }
        removed
    }
}

