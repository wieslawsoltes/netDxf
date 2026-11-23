use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::mtext_attachment_point::{MTextAttachmentPoint, MTextLineSpacingStyle};
use crate::tables::DimensionStyle;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Dimension type (`netDxf.Entities.DimensionType`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionType {
    #[default]
    Linear = 0,
    Aligned = 1,
    Angular = 2,
    Diameter = 3,
    Radius = 4,
    Angular3Point = 5,
    Ordinate = 6,
    ArcLength = 8,
}

impl DimensionType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v & 0x0F {
            0 => Self::Linear,
            1 => Self::Aligned,
            2 => Self::Angular,
            3 => Self::Diameter,
            4 => Self::Radius,
            5 => Self::Angular3Point,
            6 => Self::Ordinate,
            8 => Self::ArcLength,
            _ => return None,
        })
    }
}

/// Ordinate dimension axis.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum OrdinateDimensionAxis {
    #[default]
    X,
    Y,
}

/// Represents a DXF `DIMENSION` entity (Linear, Aligned, Radial, Diametric, Angular, Ordinate, ArcLength).
#[derive(Clone, Debug, PartialEq)]
pub struct Dimension {
    pub common: EntityCommon,
    pub dimension_type: DimensionType,
    pub block_name: String,
    pub style: String,
    pub definition_point: Vector3,
    pub text_reference_point: Vector2,
    pub attachment_point: MTextAttachmentPoint,
    pub line_spacing_style: MTextLineSpacingStyle,
    pub line_spacing_factor: f64,
    pub text_rotation: f64,
    pub user_text: Option<String>,
    pub elevation: f64,
    pub measurement: f64,
    // Type-specific definition points & angles
    pub first_reference_point: Vector3,
    pub second_reference_point: Vector3,
    pub arc_definition_point: Vector3,
    pub rotation: f64,
    pub oblique_angle: f64,
    pub leader_length: f64,
    pub ordinate_axis: OrdinateDimensionAxis,
    pub style_overrides_list: Vec<crate::tables::DimensionStyleOverride>,
    pub text_position_manually_set_flag: bool,
}

impl Dimension {
    pub fn linear(first_point: Vector2, second_point: Vector2, offset: f64, rotation: f64) -> Self {
        let measurement = Vector2::distance(first_point, second_point);
        let mid = Vector2::mid_point(first_point, second_point);
        Self {
            common: EntityCommon::new(EntityType::Dimension, DxfObjectCode::DIMENSION),
            dimension_type: DimensionType::Linear,
            block_name: String::new(),
            style: DimensionStyle::DEFAULT_NAME.to_string(),
            definition_point: Vector3::new(second_point.x, second_point.y + offset, 0.0),
            text_reference_point: Vector2::new(mid.x, mid.y + offset),
            attachment_point: MTextAttachmentPoint::MiddleCenter,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
            line_spacing_factor: 1.0,
            text_rotation: 0.0,
            user_text: None,
            elevation: 0.0,
            measurement,
            first_reference_point: first_point.to_vector3(0.0),
            second_reference_point: second_point.to_vector3(0.0),
            arc_definition_point: Vector3::ZERO,
            rotation,
            oblique_angle: 0.0,
            leader_length: 0.0,
            ordinate_axis: OrdinateDimensionAxis::X,
            style_overrides_list: Vec::new(),
            text_position_manually_set_flag: false,
        }
    }

    pub fn aligned(first_point: Vector2, second_point: Vector2, offset: f64) -> Self {
        let mut d = Self::linear(first_point, second_point, offset, 0.0);
        d.dimension_type = DimensionType::Aligned;
        d
    }

    pub fn radial(center: Vector2, reference_point: Vector2) -> Self {
        let measurement = Vector2::distance(center, reference_point);
        Self {
            common: EntityCommon::new(EntityType::Dimension, DxfObjectCode::DIMENSION),
            dimension_type: DimensionType::Radius,
            block_name: String::new(),
            style: DimensionStyle::DEFAULT_NAME.to_string(),
            definition_point: center.to_vector3(0.0),
            text_reference_point: Vector2::mid_point(center, reference_point),
            attachment_point: MTextAttachmentPoint::MiddleCenter,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
            line_spacing_factor: 1.0,
            text_rotation: 0.0,
            user_text: None,
            elevation: 0.0,
            measurement,
            first_reference_point: reference_point.to_vector3(0.0),
            second_reference_point: Vector3::ZERO,
            arc_definition_point: Vector3::ZERO,
            rotation: 0.0,
            oblique_angle: 0.0,
            leader_length: 0.0,
            ordinate_axis: OrdinateDimensionAxis::X,
            style_overrides_list: Vec::new(),
            text_position_manually_set_flag: false,
        }
    }

    pub fn diametric(first_point: Vector2, second_point: Vector2) -> Self {
        let mut d = Self::radial(first_point, second_point);
        d.dimension_type = DimensionType::Diameter;
        d
    }

    #[inline] pub fn block(&self) -> &str { &self.block_name }
    #[inline] pub fn style_overrides(&self) -> &[crate::tables::DimensionStyleOverride] { &self.style_overrides_list }
    #[inline] pub fn text_position_manually_set(&self) -> bool { self.text_position_manually_set_flag }

    /// Recomputes `measurement` and (unless manually positioned) `text_reference_point` from the dimension's geometry.
    pub fn update(&mut self) {
        let p1 = self.first_reference_point.to_vector2();
        let p2 = self.second_reference_point.to_vector2();
        let def = self.definition_point.to_vector2();
        match self.dimension_type {
            DimensionType::Linear => {
                let rad = self.rotation * crate::math_helper::MathHelper::DEG_TO_RAD;
                let dir = Vector2::new(rad.cos(), rad.sin());
                self.measurement = Vector2::dot_product(p2 - p1, dir).abs();
                if !self.text_position_manually_set_flag {
                    let perp = Vector2::perpendicular(dir);
                    let d1 = p1 + perp * Vector2::dot_product(def - p1, perp);
                    let d2 = p2 + perp * Vector2::dot_product(def - p2, perp);
                    self.text_reference_point = Vector2::mid_point(d1, d2);
                }
            }
            DimensionType::Aligned => {
                self.measurement = Vector2::distance(p1, p2);
                if !self.text_position_manually_set_flag {
                    let d = Vector2::normalize(p2 - p1);
                    let dir = if Vector2::is_nan(d) { Vector2::UNIT_X } else { d };
                    let perp = Vector2::perpendicular(dir);
                    let d1 = p1 + perp * Vector2::dot_product(def - p1, perp);
                    let d2 = p2 + perp * Vector2::dot_product(def - p2, perp);
                    self.text_reference_point = Vector2::mid_point(d1, d2);
                }
            }
            DimensionType::Radius | DimensionType::Diameter => {
                self.measurement = Vector2::distance(def, p1);
                if !self.text_position_manually_set_flag {
                    self.text_reference_point = Vector2::mid_point(def, p1);
                }
            }
            DimensionType::Angular | DimensionType::Angular3Point => {
                let v1 = p1 - def;
                let v2 = p2 - def;
                let a1 = v1.y.atan2(v1.x);
                let a2 = v2.y.atan2(v2.x);
                let mut diff = (a2 - a1).to_degrees().rem_euclid(360.0);
                if diff > 180.0 {
                    diff = 360.0 - diff;
                }
                self.measurement = diff;
            }
            DimensionType::Ordinate => {
                self.measurement = match self.ordinate_axis {
                    OrdinateDimensionAxis::X => p1.x,
                    OrdinateDimensionAxis::Y => p1.y,
                };
            }
            DimensionType::ArcLength => {
                let r = Vector2::distance(def, p1);
                let v1 = p1 - def;
                let v2 = p2 - def;
                let mut ang = (v2.y.atan2(v2.x) - v1.y.atan2(v1.x)).rem_euclid(std::f64::consts::TAU);
                if ang > std::f64::consts::PI {
                    ang = std::f64::consts::TAU - ang;
                }
                self.measurement = r * ang;
            }
        }
    }
}
