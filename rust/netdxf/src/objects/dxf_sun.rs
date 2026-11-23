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

impl DxfSun {
    #[inline] pub fn enabled(&self) -> bool { self.status }
    #[inline] pub fn shadows_enabled(&self) -> bool { self.shadows }
    #[inline] pub fn daylight_saving_time(&self) -> bool { self.daylight_savings }
    #[inline] pub fn color_index(&self) -> i16 { self.color.index }
    #[inline] pub fn true_color(&self) -> Option<i32> {
        self.color.use_true_color.then(|| {
            ((i32::from(self.color.r)) << 16)
                | ((i32::from(self.color.g)) << 8)
                | i32::from(self.color.b)
        })
    }
    #[inline] pub fn stored_version(&self) -> i32 { self.version }
    #[inline] pub fn stored_time(&self) -> (i32, i32) { (self.julian_day, self.time_milliseconds) }
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
    pub raw_hour_flags: Vec<i32>,
    pub page_setup: Option<String>,
    pub lock_view_viewport: bool,
    pub shade_plot_type: i16,
    pub viewports_per_page: i32,
    pub spacing_between_viewports: f64,
    pub source_version: Option<crate::header::DxfVersion>,
    pub references: Vec<String>,
}

impl DxfStoredSunStudy {
    #[inline] pub fn date_count(&self) -> usize { self.julian_dates.len() }
    #[inline] pub fn start_time(&self) -> i32 { self.start_time_seconds }
    #[inline] pub fn end_time(&self) -> i32 { self.end_time_seconds }
    #[inline] pub fn interval(&self) -> i32 { self.interval_seconds }
    #[inline] pub fn text_style(&self) -> Option<&str> { self.page_setup.as_deref() }
    #[inline] pub fn view(&self) -> Option<&str> { self.page_setup.as_deref() }
    #[inline] pub fn visual_style(&self) -> Option<&str> { self.page_setup.as_deref() }
    #[inline] pub fn payload(&self) -> &[i32] { &self.raw_hour_flags }

    pub fn select_dates(&mut self, dates: impl IntoIterator<Item = (i32, i32)>) {
        self.select_dates_from_calendar = true;
        self.julian_dates = dates.into_iter().collect();
    }

    pub fn select_date_range(
        &mut self,
        start_julian_day: i32,
        end_julian_day: i32,
        start_time_seconds: i32,
        end_time_seconds: i32,
        interval_seconds: i32,
    ) {
        self.select_dates_from_calendar = false;
        self.start_time_seconds = start_time_seconds;
        self.end_time_seconds = end_time_seconds;
        self.interval_seconds = interval_seconds.max(60);
        self.julian_dates = (start_julian_day..=end_julian_day)
            .map(|d| (d, start_time_seconds * 1000))
            .collect();
    }
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
            raw_hour_flags: Vec::new(),
            page_setup: None,
            lock_view_viewport: false,
            shade_plot_type: 0,
            viewports_per_page: 1,
            spacing_between_viewports: 0.0,
            source_version: None,
            references: Vec::new(),
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
