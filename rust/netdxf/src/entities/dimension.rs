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
        }
    }

    pub fn diametric(first_point: Vector2, second_point: Vector2) -> Self {
        let mut d = Self::radial(first_point, second_point);
        d.dimension_type = DimensionType::Diameter;
        d
    }

    #[inline] pub fn block(&self) -> &str { &self.block_name }
    #[inline] pub fn style_overrides(&self) -> &[String] { &[] }
    #[inline] pub fn text_position_manually_set(&self) -> bool { false }
    #[inline] pub fn update(&mut self) {}
}
