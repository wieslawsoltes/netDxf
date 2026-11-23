use crate::dxf_object::DxfObject;
use crate::objects::PlotSettings;

/// First-class DXF `PLOTSETTINGS` named page-setup object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfPlotSettingsObject {
    pub object: DxfObject,
    pub page_setup_name: String,
    pub settings: PlotSettings,
}

impl Default for DxfPlotSettingsObject {
    fn default() -> Self {
        Self::new("")
    }
}

impl DxfPlotSettingsObject {
    pub fn new(page_setup_name: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new("PLOTSETTINGS"),
            page_setup_name: page_setup_name.into(),
            settings: PlotSettings::default(),
        }
    }
}
