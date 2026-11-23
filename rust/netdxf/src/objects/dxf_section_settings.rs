use crate::dxf_object::DxfObject;

/// Represents a DXF `SECTIONSETTINGS` object (`netDxf.Objects.DxfSectionSettings`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfSectionSettings {
    pub object: DxfObject,
    pub current_section_type: i32,
    pub generation_settings_count: i32,
    pub type_settings: Vec<DxfSectionTypeSettings>,
}

impl Default for DxfSectionSettings {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SECTIONSETTINGS"),
            current_section_type: 1,
            generation_settings_count: 0,
            type_settings: Vec::new(),
        }
    }
}

impl DxfSectionSettings {
    pub const MAXIMUM_GEOMETRY_SETTINGS: usize = 64;
    pub const MAXIMUM_SOURCE_REFERENCES: usize = 16_384;
    pub const MAXIMUM_TYPE_SETTINGS: usize = 16;

    pub fn section_type(&self) -> i32 {
        self.current_section_type
    }

    pub fn type_settings(&self) -> &[DxfSectionTypeSettings] {
        &self.type_settings
    }

    pub fn set_type_settings(&mut self, settings: Vec<DxfSectionTypeSettings>) {
        self.generation_settings_count = settings.len() as i32;
        self.type_settings = settings;
    }
}

/// Represents a DXF `SECTIONMANAGER` object (`netDxf.Objects.DxfStoredSectionManager`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfStoredSectionManager {
    pub object: DxfObject,
    pub requires_full_update: bool,
    pub section_handles: Vec<String>,
    pub source_version: Option<crate::header::DxfVersion>,
    pub tags: Vec<(i32, String)>,
}

impl DxfStoredSectionManager {
    #[inline]
    pub fn sections(&self) -> &[String] {
        &self.section_handles
    }

    pub fn replace_sections(&mut self, handles: Vec<String>) {
        self.section_handles = handles;
    }
}

impl Default for DxfStoredSectionManager {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SECTIONMANAGER"),
            requires_full_update: false,
            section_handles: Vec::new(),
            source_version: None,
            tags: Vec::new(),
        }
    }
}

/// Represents DXF output / plot settings metadata (`netDxf.Objects.DxfOutputSettings`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfOutputSettings {
    pub object: DxfObject,
    pub name: String,
    pub printer_name: String,
    pub paper_size: String,
    pub scale_to_fit: bool,
}

impl Default for DxfOutputSettings {
    fn default() -> Self {
        Self {
            object: DxfObject::new("OUTPUTSETTINGS"),
            name: String::new(),
            printer_name: String::new(),
            paper_size: String::new(),
            scale_to_fit: false,
        }
    }
}

/// Per-geometry-category visual settings inside [`DxfSectionTypeSettings`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfSectionGeometrySettings {
    pub section_type: i32,
    pub geometry_type: i32,
    pub geometry_value: i32,
    pub flags: i32,
    pub visible: bool,
    pub color_code: i32,
    pub color_index: i16,
    pub layer: String,
    pub layer_name: String,
    pub linetype: String,
    pub linetype_name: String,
    pub linetype_scale: f64,
    pub lineweight: i16,
    pub face_transparency: i32,
    pub edge_transparency: i32,
    pub hatch_pattern: String,
    pub hatch_pattern_name: String,
    pub hatch_pattern_type: i32,
    pub hatch_angle: f64,
    pub hatch_scale: f64,
    pub hatch_spacing: f64,
}

/// Per-section-type settings inside [`DxfSectionSettings`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfSectionTypeSettings {
    pub section_type: i32,
    pub is_generation_option: bool,
    pub source_objects: Vec<String>,
    pub destination_block_handle: Option<String>,
    pub destination_file_name: String,
    pub geometry_settings: Vec<DxfSectionGeometrySettings>,
    pub generation_options: i32,
    pub repeat_geometry_markers: bool,
}

impl DxfSectionTypeSettings {
    #[inline]
    pub fn destination_block(&self) -> Option<&str> {
        self.destination_block_handle.as_deref()
    }
}
