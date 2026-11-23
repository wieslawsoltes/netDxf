use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

/// DXF `LIGHT` type (group code 70).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum LightType {
    #[default]
    Distant = 1,
    Point = 2,
    Spot = 3,
}

impl LightType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            1 => Self::Distant,
            2 => Self::Point,
            3 => Self::Spot,
            _ => return None,
        })
    }
}

/// Represents a DXF `LIGHT` entity (`netDxf.Entities.Light`).
#[derive(Clone, Debug, PartialEq)]
pub struct Light {
    pub common: EntityCommon,
    pub name: String,
    pub light_type: LightType,
    pub status: bool,
    pub plot_glyph: bool,
    pub intensity: f64,
    pub position: Vector3,
    pub target: Vector3,
    pub attenuation_type: i16,
    pub use_attenuation_limits: bool,
    pub attenuation_start_limit: f64,
    pub attenuation_end_limit: f64,
    pub hotspot_angle: f64,
    pub falloff_angle: f64,
    pub cast_shadows: bool,
}

impl Light {
    pub fn new(name: impl Into<String>, light_type: LightType, position: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Light, DxfObjectCode::LIGHT),
            name: name.into(),
            light_type,
            status: true,
            plot_glyph: false,
            intensity: 1.0,
            position,
            target: Vector3::ZERO,
            attenuation_type: 0,
            use_attenuation_limits: false,
            attenuation_start_limit: 0.0,
            attenuation_end_limit: 1.0,
            hotspot_angle: 45.0,
            falloff_angle: 50.0,
            cast_shadows: true,
        }
    }
}
