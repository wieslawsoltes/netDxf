use std::fs;
use std::path::Path;

use bitflags::bitflags;

use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::error::{DxfError, Result};
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

bitflags! {
    /// Hatch boundary path flags (group code 92).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct HatchBoundaryPathTypeFlags: i32 {
        const DEFAULT = 0;
        const EXTERNAL = 1;
        const POLYLINE = 2;
        const DERIVED = 4;
        const TEXTBOX = 8;
        const OUTERMOST = 16;
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum HatchFillType {
    PatternFill = 0,
    #[default]
    SolidFill = 1,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum HatchStyle {
    #[default]
    Normal = 0,
    Outer = 1,
    Ignore = 2,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum HatchType {
    UserDefined = 0,
    #[default]
    Predefined = 1,
    Custom = 2,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum HatchGradientPatternType {
    #[default]
    Linear,
    Cylinder,
    InvCylinder,
    Spherical,
    Hemispherical,
    Curved,
    InvSpherical,
    InvHemispherical,
    InvCurved,
}

impl HatchGradientPatternType {
    pub const fn as_dxf_name(self) -> &'static str {
        match self {
            Self::Linear => "LINEAR",
            Self::Cylinder => "CYLINDER",
            Self::InvCylinder => "INVCYLINDER",
            Self::Spherical => "SPHERICAL",
            Self::Hemispherical => "HEMISPHERICAL",
            Self::Curved => "CURVED",
            Self::InvSpherical => "INVSPHERICAL",
            Self::InvHemispherical => "INVHEMISPHERICAL",
            Self::InvCurved => "INVCURVED",
        }
    }
}

/// Pattern line definition for a hatch pattern.
#[derive(Clone, Debug, PartialEq)]
pub struct HatchPatternLineDefinition {
    pub angle: f64,
    pub origin: Vector2,
    pub delta: Vector2,
    pub dash_pattern: Vec<f64>,
}

/// Hatch pattern definition.
#[derive(Clone, Debug, PartialEq)]
pub struct HatchPattern {
    pub name: String,
    pub description: String,
    pub fill: HatchFillType,
    pub hatch_type: HatchType,
    pub style: HatchStyle,
    pub angle: f64,
    pub scale: f64,
    pub origin: Vector2,
    pub is_double: bool,
    pub line_definitions: Vec<HatchPatternLineDefinition>,
}

impl HatchPattern {
    pub fn solid() -> Self {
        Self {
            name: "SOLID".to_string(),
            description: "Solid fill".to_string(),
            fill: HatchFillType::SolidFill,
            hatch_type: HatchType::Predefined,
            style: HatchStyle::Normal,
            angle: 0.0,
            scale: 1.0,
            origin: Vector2::ZERO,
            is_double: false,
            line_definitions: Vec::new(),
        }
    }

    pub fn line() -> Self {
        let mut pat = Self::new("LINE", "Parallel horizontal lines");
        pat.line_definitions.push(HatchPatternLineDefinition {
            angle: 0.0,
            origin: Vector2::ZERO,
            delta: Vector2::new(0.0, 0.125),
            dash_pattern: Vec::new(),
        });
        pat
    }

    pub fn net() -> Self {
        let mut pat = Self::new("NET", "Horizontal / vertical grid");
        pat.line_definitions.push(HatchPatternLineDefinition {
            angle: 0.0,
            origin: Vector2::ZERO,
            delta: Vector2::new(0.0, 0.125),
            dash_pattern: Vec::new(),
        });
        pat.line_definitions.push(HatchPatternLineDefinition {
            angle: 90.0,
            origin: Vector2::ZERO,
            delta: Vector2::new(0.0, 0.125),
            dash_pattern: Vec::new(),
        });
        pat
    }

    pub fn dots() -> Self {
        let mut pat = Self::new("DOTS", "A series of dots");
        pat.line_definitions.push(HatchPatternLineDefinition {
            angle: 0.0,
            origin: Vector2::ZERO,
            delta: Vector2::new(0.03125, 0.0625),
            dash_pattern: vec![0.0, -0.0625],
        });
        pat
    }

    pub fn new(name: impl Into<String>, description: impl Into<String>) -> Self {
        Self {
            name: name.into(),
            description: description.into(),
            fill: HatchFillType::PatternFill,
            hatch_type: HatchType::Predefined,
            style: HatchStyle::Normal,
            angle: 0.0,
            scale: 1.0,
            origin: Vector2::ZERO,
            is_double: false,
            line_definitions: Vec::new(),
        }
    }

    #[inline]
    pub fn pattern_type(&self) -> HatchType {
        self.hatch_type
    }

    /// Lists all pattern names defined in an AutoCAD `.pat` file.
    pub fn names_from_file(path: impl AsRef<Path>) -> Result<Vec<String>> {
        let text = fs::read_to_string(path)?;
        let mut names = Vec::new();
        for line in text.lines() {
            let trimmed = line.trim();
            if let Some(rest) = trimmed.strip_prefix('*') {
                if let Some(name) = rest.split(',').next() {
                    let n = name.trim();
                    if !n.is_empty() {
                        names.push(n.to_string());
                    }
                }
            }
        }
        Ok(names)
    }

    /// Loads a named hatch pattern from an AutoCAD `.pat` file.
    pub fn load(path: impl AsRef<Path>, pattern_name: &str) -> Result<Self> {
        let text = fs::read_to_string(path)?;
        let mut current: Option<Self> = None;
        for raw_line in text.lines() {
            let line = raw_line.split(';').next().unwrap_or("").trim();
            if line.is_empty() {
                continue;
            }
            if let Some(rest) = line.strip_prefix('*') {
                if let Some(pat) = current.take() {
                    return Ok(pat);
                }
                let mut parts = rest.splitn(2, ',');
                let name = parts.next().unwrap_or("").trim();
                let desc = parts.next().unwrap_or("").trim();
                if name.eq_ignore_ascii_case(pattern_name) {
                    current = Some(Self::new(name, desc));
                }
            } else if let Some(pat) = current.as_mut() {
                let nums: Vec<f64> = line
                    .split(',')
                    .filter_map(|s| s.trim().parse::<f64>().ok())
                    .collect();
                if nums.len() >= 5 {
                    pat.line_definitions.push(HatchPatternLineDefinition {
                        angle: nums[0],
                        origin: Vector2::new(nums[1], nums[2]),
                        delta: Vector2::new(nums[3], nums[4]),
                        dash_pattern: nums[5..].to_vec(),
                    });
                }
            }
        }
        current.ok_or_else(|| {
            DxfError::InvalidValue(format!("Hatch pattern '{pattern_name}' not found in .pat file"))
        })
    }

    /// Saves this hatch pattern to an AutoCAD `.pat` file.
    pub fn save(&self, path: impl AsRef<Path>) -> Result<()> {
        let mut out = format!("*{},{}\n", self.name, self.description);
        for ld in &self.line_definitions {
            out.push_str(&format!(
                "{}, {}, {}, {}, {}",
                ld.angle, ld.origin.x, ld.origin.y, ld.delta.x, ld.delta.y
            ));
            for d in &ld.dash_pattern {
                out.push_str(&format!(", {d}"));
            }
            out.push('\n');
        }
        fs::write(path, out)?;
        Ok(())
    }
}

/// Gradient fill settings for a `Hatch`.
#[derive(Clone, Debug, PartialEq)]
pub struct HatchGradientPattern {
    pub gradient_type: HatchGradientPatternType,
    pub color1: AciColor,
    pub color2: AciColor,
    pub single_color: bool,
    pub tint: f64,
    pub angle: f64,
    pub centered: bool,
}

impl HatchGradientPattern {
    pub fn single_color_gradient(
        color: AciColor,
        tint: f64,
        gradient_type: HatchGradientPatternType,
    ) -> Self {
        Self {
            gradient_type,
            color1: color,
            color2: color,
            single_color: true,
            tint: tint.clamp(0.0, 1.0),
            angle: 0.0,
            centered: true,
        }
    }

    pub fn two_color_gradient(
        color1: AciColor,
        color2: AciColor,
        gradient_type: HatchGradientPatternType,
    ) -> Self {
        Self {
            gradient_type,
            color1,
            color2,
            single_color: false,
            tint: 1.0,
            angle: 0.0,
            centered: true,
        }
    }

    pub fn color1_aci_index(&self) -> i16 {
        self.color1.index
    }

    pub fn color2_aci_index(&self) -> i16 {
        self.color2.index
    }
}

/// An edge in a `HatchBoundaryPath`.
#[derive(Clone, Debug, PartialEq)]
pub enum HatchBoundaryPathEdge {
    Line {
        start: Vector2,
        end: Vector2,
    },
    Arc {
        center: Vector2,
        radius: f64,
        start_angle: f64,
        end_angle: f64,
        is_counter_clockwise: bool,
    },
    Ellipse {
        center: Vector2,
        end_major_axis: Vector2,
        minor_ratio: f64,
        start_angle: f64,
        end_angle: f64,
        is_counter_clockwise: bool,
    },
    Spline {
        degree: i32,
        is_rational: bool,
        is_periodic: bool,
        knots: Vec<f64>,
        control_points: Vec<(Vector2, f64)>,
        fit_points: Vec<Vector2>,
    },
    Polyline {
        vertexes: Vec<(Vector2, f64)>,
        is_closed: bool,
    },
}

/// A boundary path loop for a `Hatch` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct HatchBoundaryPath {
    pub path_type_flags: HatchBoundaryPathTypeFlags,
    pub edges: Vec<HatchBoundaryPathEdge>,
}

impl HatchBoundaryPath {
    pub fn new(edges: Vec<HatchBoundaryPathEdge>) -> Self {
        let is_poly = matches!(edges.as_slice(), [HatchBoundaryPathEdge::Polyline { .. }]);
        let flags = if is_poly {
            HatchBoundaryPathTypeFlags::POLYLINE | HatchBoundaryPathTypeFlags::DERIVED
        } else {
            HatchBoundaryPathTypeFlags::DERIVED
        };
        Self {
            path_type_flags: flags,
            edges,
        }
    }
}

/// Represents a DXF `HATCH` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Hatch {
    pub common: EntityCommon,
    pub pattern: HatchPattern,
    pub gradient: Option<HatchGradientPattern>,
    pub boundary_paths: Vec<HatchBoundaryPath>,
    pub elevation: f64,
    pub associative: bool,
    pub pixel_size: f64,
    pub seed_points: Vec<Vector2>,
    pub boundary_source_handles: Vec<String>,
}

impl Hatch {
    pub fn new(pattern: HatchPattern, boundary_paths: Vec<HatchBoundaryPath>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Hatch, DxfObjectCode::HATCH),
            pattern,
            gradient: None,
            boundary_paths,
            elevation: 0.0,
            associative: false,
            pixel_size: 0.0,
            seed_points: Vec::new(),
            boundary_source_handles: Vec::new(),
        }
    }

    /// Creates a `HatchBoundaryPath` from a slice of 2D/3D entities (`Line`, `Arc`, `Circle`, `Ellipse`, `Polyline2D`).
    pub fn create_boundary(&self, entities: &[EntityObject]) -> HatchBoundaryPath {
        let mut edges = Vec::new();
        for ent in entities {
            match ent {
                EntityObject::Line(l) => {
                    let (s, _) = MathHelper::transform_wcs_to_ocs(l.start_point, self.common.normal);
                    let (e, _) = MathHelper::transform_wcs_to_ocs(l.end_point, self.common.normal);
                    edges.push(HatchBoundaryPathEdge::Line { start: s, end: e });
                }
                EntityObject::Arc(a) => {
                    let (c, _) = MathHelper::transform_wcs_to_ocs(a.center, self.common.normal);
                    edges.push(HatchBoundaryPathEdge::Arc {
                        center: c,
                        radius: a.radius,
                        start_angle: a.start_angle,
                        end_angle: a.end_angle,
                        is_counter_clockwise: true,
                    });
                }
                EntityObject::Circle(c) => {
                    let (ctr, _) = MathHelper::transform_wcs_to_ocs(c.center, self.common.normal);
                    edges.push(HatchBoundaryPathEdge::Arc {
                        center: ctr,
                        radius: c.radius,
                        start_angle: 0.0,
                        end_angle: 360.0,
                        is_counter_clockwise: true,
                    });
                }
                EntityObject::Ellipse(e) => {
                    let (ctr, _) = MathHelper::transform_wcs_to_ocs(e.center, self.common.normal);
                    let (maj, _) = MathHelper::transform_wcs_to_ocs(
                        e.major_axis_endpoint_wcs(),
                        self.common.normal,
                    );
                    edges.push(HatchBoundaryPathEdge::Ellipse {
                        center: ctr,
                        end_major_axis: maj,
                        minor_ratio: e.minor_to_major_ratio(),
                        start_angle: e.start_angle,
                        end_angle: e.end_angle,
                        is_counter_clockwise: true,
                    });
                }
                EntityObject::Polyline2D(p) => {
                    let verts = p.vertexes.iter().map(|v| (v.position, v.bulge)).collect();
                    edges.push(HatchBoundaryPathEdge::Polyline {
                        vertexes: verts,
                        is_closed: p.is_closed,
                    });
                }
                _ => {}
            }
        }
        HatchBoundaryPath::new(edges)
    }

    /// Associates source entity handles with this hatch.
    pub fn set_boundary_sources(&mut self, handles: impl IntoIterator<Item = String>) {
        self.boundary_source_handles = handles.into_iter().collect();
        self.associative = !self.boundary_source_handles.is_empty();
    }

    /// Unlinks associative boundary entity references.
    pub fn unlink_boundary(&mut self) {
        self.boundary_source_handles.clear();
        self.associative = false;
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let new_normal = Vector3::normalize_finite_direction(transformation * self.common.normal)
            .unwrap_or(Vector3::UNIT_Z);
        let xform_pt = |p: Vector2| -> Vector2 {
            let wcs = transformation
                * MathHelper::transform_ocs_to_wcs(p, self.common.normal, self.elevation)
                + translation;
            MathHelper::transform_wcs_to_ocs(wcs, new_normal).0
        };
        for path in &mut self.boundary_paths {
            for edge in &mut path.edges {
                match edge {
                    HatchBoundaryPathEdge::Line { start, end } => {
                        *start = xform_pt(*start);
                        *end = xform_pt(*end);
                    }
                    HatchBoundaryPathEdge::Arc { center, .. } => {
                        *center = xform_pt(*center);
                    }
                    HatchBoundaryPathEdge::Ellipse { center, .. } => {
                        *center = xform_pt(*center);
                    }
                    HatchBoundaryPathEdge::Spline {
                        control_points,
                        fit_points,
                        ..
                    } => {
                        for (pt, _) in control_points {
                            *pt = xform_pt(*pt);
                        }
                        for pt in fit_points {
                            *pt = xform_pt(*pt);
                        }
                    }
                    HatchBoundaryPathEdge::Polyline { vertexes, .. } => {
                        for (pt, _) in vertexes {
                            *pt = xform_pt(*pt);
                        }
                    }
                }
            }
        }
        for sp in &mut self.seed_points {
            *sp = xform_pt(*sp);
        }
        self.common.normal = new_normal;
    }
}

/// Hatch boundary path edge type discriminator (DXF group 72).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EdgeType {
    Polyline = 0,
    Line = 1,
    Arc = 2,
    Ellipse = 3,
    Spline = 4,
}

/// Alias matching upstream nested `HatchBoundaryPath.Edge`.
pub type Edge = HatchBoundaryPathEdge;

/// Polyline boundary path edge matching upstream nested `HatchBoundaryPath.Polyline`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Polyline {
    pub vertexes: Vec<Vector3>,
    pub is_closed: bool,
}
