use crate::aci_color::AciColor;
use crate::dxf_object::DxfObject;

/// Represents a DXF `SUN` object (`netDxf.Objects.DxfSun`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfSun {
    pub object: DxfObject,
    pub version: i32,
    pub status: bool,
    pub color: AciColor,
    pub intensity: f64,
    pub shadows: bool,
    pub julian_day: i32,
    pub time_milliseconds: i32,
    pub daylight_savings: bool,
    pub shadow_type: i32,
    pub shadow_map_size: i16,
    pub shadow_softness: i16,
}

impl Default for DxfSun {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SUN"),
            version: 1,
            status: true,
            color: AciColor::DEFAULT,
            intensity: 1.0,
            shadows: true,
            julian_day: 2_454_000,
            time_milliseconds: 43_200_000, // 12:00 noon
            daylight_savings: false,
            shadow_type: 0,
            shadow_map_size: 256,
            shadow_softness: 1,
        }
    }
}

/// Represents a DXF `SUNSTUDY` object (`netDxf.Objects.DxfStoredSunStudy`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfStoredSunStudy {
    pub object: DxfObject,
    pub version: i32,
    pub name: String,
    pub description: String,
    pub output_type: i16,
    pub sheet_set_name: String,
    pub use_subset: bool,
    pub sheet_subset_name: String,
    pub select_dates_from_calendar: bool,
    pub julian_dates: Vec<(i32, i32)>,
    pub start_time_seconds: i32,
    pub end_time_seconds: i32,
    pub interval_seconds: i32,
    pub lock_view_viewport: bool,
    pub shade_plot_type: i16,
    pub viewports_per_page: i32,
    pub spacing_between_viewports: f64,
}

impl Default for DxfStoredSunStudy {
    fn default() -> Self {
        Self {
            object: DxfObject::new("SUNSTUDY"),
            version: 1,
            name: String::new(),
            description: String::new(),
            output_type: 0,
            sheet_set_name: String::new(),
            use_subset: false,
            sheet_subset_name: String::new(),
            select_dates_from_calendar: false,
            julian_dates: Vec::new(),
            start_time_seconds: 28_800,
            end_time_seconds: 61_200,
            interval_seconds: 3_600,
            lock_view_viewport: false,
            shade_plot_type: 0,
            viewports_per_page: 1,
            spacing_between_viewports: 0.0,
        }
    }
}

/// Shadow rendering mode for [`DxfSun`] (DXF group 70).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum DxfSunShadowType {
    #[default]
    RayTraced = 0,
    ShadowMaps = 1,
    AreaSampled = 2,
}
