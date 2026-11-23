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
    pub sea_level_correction: bool,
    pub coordinate_projection_radius: f64,
    pub coordinate_system_definition: String,
    pub geo_rss_tag: String,
    pub observation_from_tag: String,
    pub observation_to_tag: String,
    pub observation_coverage_tag: String,
    pub mesh_points: Vec<DxfGeoMeshPoint>,
    pub mesh_faces: Vec<DxfGeoMeshFace>,
}

impl DxfGeoData {
    #[inline] pub fn host_block(&self) -> Option<&str> { self.host_block_handle.as_deref() }
    #[inline] pub fn coordinate_type(&self) -> DxfGeoCoordinateType {
        match self.design_coordinates_type {
            1 => DxfGeoCoordinateType::LocalGrid,
            2 => DxfGeoCoordinateType::ProjectedGrid,
            3 => DxfGeoCoordinateType::Geographic,
            _ => DxfGeoCoordinateType::Unknown,
        }
    }
    #[inline] pub fn scale_estimation(&self) -> DxfGeoScaleEstimation {
        match self.scale_estimation_method {
            2 => DxfGeoScaleEstimation::UserScale,
            3 => DxfGeoScaleEstimation::GridScale,
            4 => DxfGeoScaleEstimation::Prismoidal,
            _ => DxfGeoScaleEstimation::None,
        }
    }
    #[inline] pub fn observation_from(&self) -> &str { &self.observation_from_tag }
    #[inline] pub fn observation_to(&self) -> &str { &self.observation_to_tag }
    #[inline] pub fn observation_coverage(&self) -> &str { &self.observation_coverage_tag }
    #[inline] pub fn sea_level_elevation(&self) -> f64 { self.reference_point.z }

    pub fn set_mesh(&mut self, points: Vec<DxfGeoMeshPoint>, faces: Vec<DxfGeoMeshFace>) {
        self.mesh_points = points;
        self.mesh_faces = faces;
    }
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
            sea_level_correction: false,
            coordinate_projection_radius: 0.0,
            coordinate_system_definition: String::new(),
            geo_rss_tag: String::new(),
            observation_from_tag: String::new(),
            observation_to_tag: String::new(),
            observation_coverage_tag: String::new(),
            mesh_points: Vec::new(),
            mesh_faces: Vec::new(),
        }
    }
}

/// Coordinate system type stored in [`DxfGeoData`] (DXF group 70).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum DxfGeoCoordinateType {
    #[default]
    Unknown = 0,
    LocalGrid = 1,
    ProjectedGrid = 2,
    Geographic = 3,
}

/// Scale estimation method stored in [`DxfGeoData`] (DXF group 95).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum DxfGeoScaleEstimation {
    #[default]
    None = 1,
    UserScale = 2,
    GridScale = 3,
    Prismoidal = 4,
}

/// Single mesh vertex pair (source -> target) in [`DxfGeoData`].
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfGeoMeshPoint {
    pub source_x: f64,
    pub source_y: f64,
    pub target_x: f64,
    pub target_y: f64,
}

impl DxfGeoMeshPoint {
    #[inline] pub fn source(&self) -> Vector2 { Vector2::new(self.source_x, self.source_y) }
    #[inline] pub fn target(&self) -> Vector2 { Vector2::new(self.target_x, self.target_y) }
}

/// Triangular face index triple in [`DxfGeoData`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DxfGeoMeshFace {
    pub index1: i32,
    pub index2: i32,
    pub index3: i32,
}

impl DxfGeoMeshFace {
    #[inline] pub fn first(&self) -> i32 { self.index1 }
    #[inline] pub fn second(&self) -> i32 { self.index2 }
    #[inline] pub fn third(&self) -> i32 { self.index3 }
}
