use std::time::{Duration, SystemTime};

use indexmap::IndexMap;

use crate::aci_color::AciColor;
use crate::entities::mline_justification::MLineJustification;
use crate::header::att_mode::AttMode;
use crate::header::dxf_version::DxfVersion;
use crate::header::header_variable::HeaderVariable;
use crate::header::point_shape::PointShape;
use crate::lineweight::Lineweight;
use crate::units::{AngleDirection, AngleUnitType, DrawingUnits, LinearUnitType};
use crate::vector3::Vector3;

/// Represents the header variables of a DXF document (`HEADER` section).
#[derive(Clone, Debug, PartialEq)]
pub struct HeaderVariables {
    pub acad_ver: DxfVersion,
    pub dwg_code_page: String,
    pub last_saved_by: String,
    pub handle_seed: String,
    pub angbase: f64,
    pub angdir: AngleDirection,
    pub att_mode: AttMode,
    pub a_units: AngleUnitType,
    pub a_uprec: i16,
    pub ce_color: AciColor,
    pub ce_lt_scale: f64,
    pub ce_ltype: String,
    pub ce_lweight: Lineweight,
    pub c_layer: String,
    pub cml_just: MLineJustification,
    pub cml_scale: f64,
    pub cml_style: String,
    pub dim_style: String,
    pub text_size: f64,
    pub text_style: String,
    pub l_units: LinearUnitType,
    pub l_uprec: i16,
    pub mirr_text: bool,
    pub extnames: bool,
    pub ins_base: Vector3,
    pub ins_units: DrawingUnits,
    pub lt_scale: f64,
    pub lw_display: bool,
    pub pd_mode: PointShape,
    pub pd_size: f64,
    pub p_line_gen: i16,
    pub ps_lt_scale: i16,
    pub spline_segs: i16,
    pub surf_u: i16,
    pub surf_v: i16,
    pub td_create: SystemTime,
    pub tdu_create: SystemTime,
    pub td_update: SystemTime,
    pub tdu_update: SystemTime,
    pub tdin_dwg: Duration,
    pub ucs_org: Vector3,
    pub ucs_x_dir: Vector3,
    pub ucs_y_dir: Vector3,
    pub custom_variables: IndexMap<String, HeaderVariable>,
}

impl Default for HeaderVariables {
    fn default() -> Self {
        let now = SystemTime::now();
        Self {
            acad_ver: DxfVersion::AutoCad2000,
            dwg_code_page: "ANSI_1252".to_string(),
            last_saved_by: String::new(),
            handle_seed: "1".to_string(),
            angbase: 0.0,
            angdir: AngleDirection::CCW,
            att_mode: AttMode::Normal,
            a_units: AngleUnitType::DecimalDegrees,
            a_uprec: 0,
            ce_color: AciColor::BY_LAYER,
            ce_lt_scale: 1.0,
            ce_ltype: "ByLayer".to_string(),
            ce_lweight: Lineweight::ByLayer,
            c_layer: "0".to_string(),
            cml_just: MLineJustification::Top,
            cml_scale: 20.0,
            cml_style: "Standard".to_string(),
            dim_style: "Standard".to_string(),
            text_size: 2.5,
            text_style: "Standard".to_string(),
            l_units: LinearUnitType::Decimal,
            l_uprec: 4,
            mirr_text: false,
            extnames: true,
            ins_base: Vector3::ZERO,
            ins_units: DrawingUnits::Unitless,
            lt_scale: 1.0,
            lw_display: false,
            pd_mode: PointShape::Dot,
            pd_size: 0.0,
            p_line_gen: 0,
            ps_lt_scale: 1,
            spline_segs: 8,
            surf_u: 6,
            surf_v: 6,
            td_create: now,
            tdu_create: now,
            td_update: now,
            tdu_update: now,
            tdin_dwg: Duration::ZERO,
            ucs_org: Vector3::ZERO,
            ucs_x_dir: Vector3::UNIT_X,
            ucs_y_dir: Vector3::UNIT_Y,
            custom_variables: IndexMap::new(),
        }
    }
}

impl HeaderVariables {
    pub fn with_version(version: DxfVersion) -> Self {
        Self {
            acad_ver: version,
            ..Self::default()
        }
    }

    /// Adds or replaces a custom header variable (`$...`).
    pub fn add_custom_variable(&mut self, variable: HeaderVariable) {
        self.custom_variables.insert(variable.name.clone(), variable);
    }

    /// Removes a custom header variable by name.
    pub fn remove_custom_variable(&mut self, name: &str) -> Option<HeaderVariable> {
        self.custom_variables.shift_remove(name)
    }

    /// Returns `true` if a custom header variable with `name` exists.
    pub fn contains_custom_variable(&self, name: &str) -> bool {
        self.custom_variables.contains_key(name)
    }

    /// Looks up a custom header variable by name.
    pub fn try_get_custom_variable(&self, name: &str) -> Option<&HeaderVariable> {
        self.custom_variables.get(name)
    }

    /// Clears all custom header variables.
    pub fn clear_custom_variables(&mut self) {
        self.custom_variables.clear();
    }

    /// Returns the names of all custom header variables.
    pub fn custom_names(&self) -> Vec<&str> {
        self.custom_variables.keys().map(|s| s.as_str()).collect()
    }

    /// Returns all custom header variables.
    pub fn custom_values(&self) -> Vec<&HeaderVariable> {
        self.custom_variables.values().collect()
    }

    /// Returns the canonical known DXF header variable names.
    pub fn known_names() -> &'static [&'static str] {
        &[
            "$ACADVER", "$DWGCODEPAGE", "$LASTSAVEDBY", "$HANDSEED", "$ANGBASE", "$ANGDIR",
            "$ATTMODE", "$AUNITS", "$AUPREC", "$CECOLOR", "$CELTSCALE", "$CELTYPE", "$CELWEIGHT",
            "$CLAYER", "$CMLJUST", "$CMLSCALE", "$CMLSTYLE", "$DIMSTYLE", "$TEXTSIZE", "$TEXTSTYLE",
            "$LUNITS", "$LUPREC", "$MIRRTEXT", "$EXTNAMES", "$INSBASE", "$INSUNITS", "$LTSCALE",
            "$LWDISPLAY", "$PDMODE", "$PDSIZE", "$PLINEGEN", "$PSLTSCALE", "$SPLINESEGS", "$SURFU",
            "$SURFV", "$TDCREATE", "$TDUCREATE", "$TDUPDATE", "$TDUUPDATE", "$TDINDWG", "$UCSORG",
            "$UCSXDIR", "$UCSYDIR",
        ]
    }

    /// Returns snapshots of the core known header variables as `HeaderVariable` values.
    pub fn known_values(&self) -> Vec<HeaderVariable> {
        use crate::header::header_variable::HeaderVariableValue;
        vec![
            HeaderVariable::new("$ACADVER", 1, HeaderVariableValue::String(self.acad_ver.as_code().to_string())),
            HeaderVariable::new("$DWGCODEPAGE", 3, HeaderVariableValue::String(self.dwg_code_page.clone())),
            HeaderVariable::new("$HANDSEED", 5, HeaderVariableValue::String(self.handle_seed.clone())),
            HeaderVariable::new("$CLAYER", 8, HeaderVariableValue::String(self.c_layer.clone())),
            HeaderVariable::new("$DIMSTYLE", 2, HeaderVariableValue::String(self.dim_style.clone())),
            HeaderVariable::new("$TEXTSTYLE", 7, HeaderVariableValue::String(self.text_style.clone())),
            HeaderVariable::new("$TEXTSIZE", 40, HeaderVariableValue::Float(self.text_size)),
            HeaderVariable::new("$LTSCALE", 40, HeaderVariableValue::Float(self.lt_scale)),
            HeaderVariable::new("$UCSORG", 10, HeaderVariableValue::Vector3(self.ucs_org)),
            HeaderVariable::new("$UCSXDIR", 10, HeaderVariableValue::Vector3(self.ucs_x_dir)),
            HeaderVariable::new("$UCSYDIR", 10, HeaderVariableValue::Vector3(self.ucs_y_dir)),
        ]
    }

    /// Constructs the current `UCS` from `$UCSORG`, `$UCSXDIR`, and `$UCSYDIR`.
    pub fn current_ucs(&self) -> crate::tables::UCS {
        crate::tables::UCS::from_origin_axes(
            "CurrentUCS",
            self.ucs_org,
            self.ucs_x_dir,
            self.ucs_y_dir,
        )
        .unwrap_or_else(|_| crate::tables::UCS::new("CurrentUCS").expect("valid UCS name"))
    }
}
