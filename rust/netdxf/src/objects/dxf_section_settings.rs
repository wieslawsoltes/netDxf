use crate::dxf_object::DxfObject;

/// Represents a DXF `SECTIONSETTINGS` object (`netDxf.Objects.DxfSectionSettings`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfSectionSettings {
    pub object: DxfObject,
    pub current_section_type: i32,
    pub generation_settings_count: i32,
}

impl Default for DxfSectionSettings {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SECTIONSETTINGS"),
            current_section_type: 1,
            generation_settings_count: 0,
        }
    }
}

/// Represents a DXF `SECTIONMANAGER` object (`netDxf.Objects.DxfStoredSectionManager`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfStoredSectionManager {
    pub object: DxfObject,
    pub requires_full_update: bool,
    pub section_handles: Vec<String>,
}

impl Default for DxfStoredSectionManager {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SECTIONMANAGER"),
            requires_full_update: false,
            section_handles: Vec::new(),
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
