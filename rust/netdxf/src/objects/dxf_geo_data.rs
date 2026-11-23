use crate::dxf_object::DxfObject;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `GEODATA` object (`netDxf.Objects.DxfGeoData`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfGeoData {
    pub object: DxfObject,
    pub version: i32,
    pub host_block_handle: Option<String>,
    pub design_coordinates_type: i16,
    pub design_point: Vector3,
    pub reference_point: Vector3,
    pub horizontal_unit_scale: f64,
    pub horizontal_units: i32,
    pub vertical_unit_scale: f64,
    pub vertical_units: i32,
    pub up_direction: Vector3,
    pub north_direction: Vector2,
    pub scale_estimation_method: i32,
    pub user_scale_factor: f64,
    pub coordinate_system_definition: String,
    pub geo_rss_tag: String,
    pub observation_from_tag: String,
    pub observation_to_tag: String,
    pub observation_coverage_tag: String,
}

impl Default for DxfGeoData {
    fn default() -> Self {
        Self {
            object: DxfObject::new("GEODATA"),
            version: 2,
            host_block_handle: None,
            design_coordinates_type: 0,
            design_point: Vector3::ZERO,
            reference_point: Vector3::ZERO,
            horizontal_unit_scale: 1.0,
            horizontal_units: 6,
            vertical_unit_scale: 1.0,
            vertical_units: 6,
            up_direction: Vector3::UNIT_Z,
            north_direction: Vector2::UNIT_Y,
            scale_estimation_method: 1,
            user_scale_factor: 1.0,
            coordinate_system_definition: String::new(),
            geo_rss_tag: String::new(),
            observation_from_tag: String::new(),
            observation_to_tag: String::new(),
            observation_coverage_tag: String::new(),
        }
    }
}
