use bitflags::bitflags;

use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector2::Vector2;

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
            line_definitions: Vec::new(),
        }
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
            line_definitions: Vec::new(),
        }
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
    pub seed_points: Vec<Vector2>,
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
            seed_points: Vec::new(),
        }
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
    pub vertexes: Vec<crate::vector3::Vector3>,
    pub is_closed: bool,
}
