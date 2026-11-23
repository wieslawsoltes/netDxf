use crate::vector2::Vector2;

/// Plot paper units (group code 72).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum PlotPaperUnits {
    Inches = 0,
    #[default]
    Millimeters = 1,
    Pixels = 2,
}

/// Plot rotation (group code 73).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum PlotRotation {
    #[default]
    NoRotation = 0,
    Degrees90 = 1,
    Degrees180 = 2,
    Degrees270 = 3,
}

/// Paper margins in millimeters.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PaperMargin {
    pub left: f64,
    pub bottom: f64,
    pub right: f64,
    pub top: f64,
}

impl Default for PaperMargin {
    fn default() -> Self {
        Self { left: 7.5, bottom: 20.0, right: 7.5, top: 20.0 }
    }
}

/// Represents DXF `PLOTSETTINGS` for a `Layout`.
#[derive(Clone, Debug, PartialEq)]
pub struct PlotSettings {
    pub page_setup_name: String,
    pub plotter_name: String,
    pub paper_size_name: String,
    pub paper_margin: PaperMargin,
    pub paper_size: Vector2,
    pub origin: Vector2,
    pub paper_units: PlotPaperUnits,
    pub paper_rotation: PlotRotation,
    pub print_scale_numerator: f64,
    pub print_scale_denominator: f64,
}

impl Default for PlotSettings {
    fn default() -> Self {
        Self {
            page_setup_name: String::new(),
            plotter_name: "none_device".to_string(),
            paper_size_name: "ISO_A4_(210.00_x_297.00_MM)".to_string(),
            paper_margin: PaperMargin::default(),
            paper_size: Vector2::new(210.0, 297.0),
            origin: Vector2::ZERO,
            paper_units: PlotPaperUnits::Millimeters,
            paper_rotation: PlotRotation::Degrees90,
            print_scale_numerator: 1.0,
            print_scale_denominator: 1.0,
        }
    }
}
