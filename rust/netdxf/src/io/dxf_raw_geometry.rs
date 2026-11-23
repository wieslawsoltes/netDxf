use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Raw DXF `POINT` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawPointGeometry {
    pub position: Vector3,
    pub thickness: f64,
    pub extrusion: Vector3,
    pub angle: f64,
}

/// Raw DXF `LINE` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawLineGeometry {
    pub start: Vector3,
    pub end: Vector3,
    pub thickness: f64,
    pub extrusion: Vector3,
}

impl DxfRawLineGeometry {
    #[inline] pub fn start_point(&self) -> Vector3 { self.start }
    #[inline] pub fn end_point(&self) -> Vector3 { self.end }
    #[inline] pub fn extrusion_direction(&self) -> Vector3 { self.extrusion }
}

/// Raw DXF `CIRCLE` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawCircleGeometry {
    pub center: Vector3,
    pub radius: f64,
    pub thickness: f64,
    pub extrusion: Vector3,
}

/// Raw DXF `ARC` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawArcGeometry {
    pub center: Vector3,
    pub radius: f64,
    pub start_angle: f64,
    pub end_angle: f64,
    pub thickness: f64,
    pub extrusion: Vector3,
}

/// Raw DXF `ELLIPSE` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawEllipseGeometry {
    pub center: Vector3,
    pub major_axis_endpoint: Vector3,
    pub minor_axis_ratio: f64,
    pub start_parameter: f64,
    pub end_parameter: f64,
    pub extrusion: Vector3,
}

impl DxfRawEllipseGeometry {
    #[inline] pub fn major_axis(&self) -> Vector3 { self.major_axis_endpoint }
    #[inline] pub fn axis_ratio(&self) -> f64 { self.minor_axis_ratio }
    #[inline] pub fn extrusion_direction(&self) -> Vector3 { self.extrusion }
}

/// Raw DXF `RAY` / `XLINE` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawInfiniteLineGeometry {
    pub origin: Vector3,
    pub direction: Vector3,
    pub is_ray: bool,
}

/// Single vertex inside a [`DxfRawLwPolylineGeometry`].
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawLwPolylineVertex {
    pub position: Vector2,
    pub start_width: f64,
    pub end_width: f64,
    pub bulge: f64,
    pub identifier: i32,
}

impl DxfRawLwPolylineVertex {
    #[inline] pub fn has_start_width(&self) -> bool { self.start_width != 0.0 }
    #[inline] pub fn has_end_width(&self) -> bool { self.end_width != 0.0 }
    #[inline] pub fn has_bulge(&self) -> bool { self.bulge != 0.0 }
}

/// Raw DXF `LWPOLYLINE` geometry view.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfRawLwPolylineGeometry {
    pub vertices: Vec<DxfRawLwPolylineVertex>,
    pub is_closed: bool,
    pub constant_width: f64,
    pub elevation: f64,
    pub thickness: f64,
    pub extrusion: Vector3,
}

/// Raw DXF `3DFACE` geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawFace3DGeometry {
    pub first_vertex: Vector3,
    pub second_vertex: Vector3,
    pub third_vertex: Vector3,
    pub fourth_vertex: Vector3,
    pub edge_flags: i16,
}

/// Raw DXF `SOLID` / `TRACE` quad geometry view.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfRawQuadGeometry {
    pub first_vertex: Vector3,
    pub second_vertex: Vector3,
    pub third_vertex: Vector3,
    pub fourth_vertex: Vector3,
    pub thickness: f64,
    pub extrusion: Vector3,
}

impl DxfRawQuadGeometry {
    #[inline] pub fn first_vertex_in_object_coordinates(&self) -> Vector3 { self.first_vertex }
    #[inline] pub fn second_vertex_in_object_coordinates(&self) -> Vector3 { self.second_vertex }
    #[inline] pub fn third_vertex_in_object_coordinates(&self) -> Vector3 { self.third_vertex }
    #[inline] pub fn fourth_vertex_in_object_coordinates(&self) -> Vector3 { self.fourth_vertex }
    #[inline] pub fn extrusion_direction(&self) -> Vector3 { self.extrusion }
}
