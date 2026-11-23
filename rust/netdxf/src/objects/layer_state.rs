use std::fs;
use std::path::Path;

use indexmap::IndexMap;

use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::{DxfError, Result};
use crate::lineweight::Lineweight;
use crate::tables::{Layer, TableObject, TableObjectData};
use crate::transparency::Transparency;

/// Snapshot of a layer's properties inside a `LayerState`.
#[derive(Clone, Debug, PartialEq)]
pub struct LayerStateProperties {
    pub name: String,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_locked: bool,
    pub plot: bool,
    pub color: AciColor,
    pub linetype: String,
    pub lineweight: Lineweight,
    pub transparency: Transparency,
}

impl From<&Layer> for LayerStateProperties {
    fn from(layer: &Layer) -> Self {
        Self {
            name: layer.name().to_string(),
            is_visible: layer.is_visible,
            is_frozen: layer.is_frozen,
            is_locked: layer.is_locked,
            plot: layer.plot,
            color: layer.color,
            linetype: layer.linetype.clone(),
            lineweight: layer.lineweight,
            transparency: layer.transparency,
        }
    }
}

/// Represents a saved DXF `LayerState` (`ACAD_LAYERSTATES`).
#[derive(Clone, Debug, PartialEq)]
pub struct LayerState {
    pub table: TableObjectData,
    pub description: String,
    pub current_layer: String,
    pub paper_space: bool,
    pub restore_mask: i32,
    pub properties: IndexMap<String, LayerStateProperties>,
}

impl LayerState {
    pub const MAXIMUM_LAS_BYTES: usize = 16 * 1024 * 1024;
    pub const MAXIMUM_LAS_LAYERS: usize = 65_536;

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::XRECORD, true)?,
            description: String::new(),
            current_layer: Layer::DEFAULT_NAME.to_string(),
            paper_space: false,
            restore_mask: 0x1FFF,
            properties: IndexMap::new(),
        })
    }

    /// Loads an AutoCAD Layer State (`.las`) file from disk.
    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        let bytes = fs::read(path)?;
        if bytes.len() > Self::MAXIMUM_LAS_BYTES {
            return Err(DxfError::InvalidValue(format!(
                "LAS file exceeds maximum allowed size of {} bytes",
                Self::MAXIMUM_LAS_BYTES
            )));
        }
        let text = String::from_utf8_lossy(&bytes);
        Self::from_las_text(&text)
    }

    /// Parses an AutoCAD `.las` (DXF group-code pair) text representation into a `LayerState`.
    pub fn from_las_text(text: &str) -> Result<Self> {
        let lines: Vec<&str> = text.lines().collect();
        let mut idx = 0usize;
        let mut state_name = "LayerState".to_string();
        let mut description = String::new();
        let mut current_layer = Layer::DEFAULT_NAME.to_string();
        let mut paper_space = false;
        let mut restore_mask = 0x1FFF_i32;
        let mut properties: IndexMap<String, LayerStateProperties> = IndexMap::new();
        let mut current_prop: Option<LayerStateProperties> = None;

        while idx + 1 < lines.len() {
            let code_str = lines[idx].trim();
            let val = lines[idx + 1].trim();
            idx += 2;
            let Ok(code) = code_str.parse::<i32>() else {
                continue;
            };
            match code {
                1 => {
                    if current_prop.is_none() && !val.is_empty() {
                        state_name = val.to_string();
                    }
                }
                301 => description = val.to_string(),
                302 => current_layer = val.to_string(),
                90 => {
                    if let Ok(m) = val.parse::<i32>() {
                        restore_mask = m;
                    }
                }
                91 => {
                    if let Ok(ps) = val.parse::<i32>() {
                        paper_space = ps != 0;
                    }
                }
                8 => {
                    if let Some(prop) = current_prop.take() {
                        if properties.len() < Self::MAXIMUM_LAS_LAYERS {
                            properties.insert(prop.name.clone(), prop);
                        }
                    }
                    current_prop = Some(LayerStateProperties {
                        name: val.to_string(),
                        is_visible: true,
                        is_frozen: false,
                        is_locked: false,
                        plot: true,
                        color: AciColor::DEFAULT,
                        linetype: "Continuous".to_string(),
                        lineweight: Lineweight::Default,
                        transparency: Transparency::default(),
                    });
                }
                62 => {
                    if let (Some(prop), Ok(ci)) = (current_prop.as_mut(), val.parse::<i16>()) {
                        prop.is_visible = ci >= 0;
                        prop.color = AciColor::from_index(ci.abs()).unwrap_or(AciColor::DEFAULT);
                    }
                }
                6 => {
                    if let Some(prop) = current_prop.as_mut() {
                        prop.linetype = val.to_string();
                    }
                }
                70 => {
                    if let (Some(prop), Ok(flags)) = (current_prop.as_mut(), val.parse::<i16>()) {
                        prop.is_frozen = (flags & 1) != 0;
                        prop.is_locked = (flags & 4) != 0;
                    }
                }
                290 => {
                    if let (Some(prop), Ok(p)) = (current_prop.as_mut(), val.parse::<i16>()) {
                        prop.plot = p != 0;
                    }
                }
                370 => {
                    if let (Some(prop), Ok(lw)) = (current_prop.as_mut(), val.parse::<i16>()) {
                        prop.lineweight = Lineweight::from_i16(lw).unwrap_or(Lineweight::Default);
                    }
                }
                440 => {
                    if let (Some(prop), Ok(raw)) = (current_prop.as_mut(), val.parse::<i32>()) {
                        prop.transparency = Transparency::from_alpha_value(raw);
                    }
                }
                _ => {}
            }
        }
        if let Some(prop) = current_prop {
            if properties.len() < Self::MAXIMUM_LAS_LAYERS {
                properties.insert(prop.name.clone(), prop);
            }
        }

        let mut state = Self::new(state_name)?;
        state.description = description;
        state.current_layer = current_layer;
        state.paper_space = paper_space;
        state.restore_mask = restore_mask;
        state.properties = properties;
        Ok(state)
    }

    /// Alias for `from_las_text` matching C# `LayerState.ReadLas`.
    pub fn read_las(text: &str) -> Result<Self> {
        Self::from_las_text(text)
    }

    /// Serializes this `LayerState` to AutoCAD `.las` text format.
    pub fn to_las_text(&self) -> String {
        let mut out = String::new();
        out.push_str(&format!("1\n{}\n", self.name()));
        out.push_str(&format!("301\n{}\n", self.description));
        out.push_str(&format!("302\n{}\n", self.current_layer));
        out.push_str(&format!("90\n{}\n", self.restore_mask));
        out.push_str(&format!("91\n{}\n", i32::from(self.paper_space)));
        for prop in self.properties.values() {
            out.push_str(&format!("8\n{}\n", prop.name));
            let ci = prop.color.index;
            let signed_ci = if prop.is_visible { ci } else { -ci };
            out.push_str(&format!("62\n{signed_ci}\n"));
            out.push_str(&format!("6\n{}\n", prop.linetype));
            let flags = i16::from(prop.is_frozen) | (i16::from(prop.is_locked) << 2);
            out.push_str(&format!("70\n{flags}\n"));
            out.push_str(&format!("290\n{}\n", i16::from(prop.plot)));
            out.push_str(&format!("370\n{}\n", prop.lineweight as i16));
        }
        out
    }

    /// Captures the current properties of all `layers` into this `LayerState`.
    pub fn capture_from_layers<'a>(&mut self, layers: impl IntoIterator<Item = &'a Layer>) {
        self.properties.clear();
        for layer in layers {
            let prop = LayerStateProperties::from(layer);
            self.properties.insert(prop.name.clone(), prop);
        }
    }

    /// Applies this `LayerState`'s stored properties back to matching `layers`.
    pub fn restore_to_layers<'a>(&self, layers: impl IntoIterator<Item = &'a mut Layer>) {
        for layer in layers {
            if let Some(prop) = self.properties.get(layer.name()) {
                layer.is_visible = prop.is_visible;
                layer.is_frozen = prop.is_frozen;
                layer.is_locked = prop.is_locked;
                layer.plot = prop.plot;
                layer.color = prop.color;
                layer.linetype = prop.linetype.clone();
                layer.lineweight = prop.lineweight;
                layer.transparency = prop.transparency;
            }
        }
    }
}

impl TableObject for LayerState {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
