use bitflags::bitflags;

use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::polyline2d_vertex::Polyline2DVertex;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

bitflags! {
    /// Polyline flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct PolylineTypeFlags: i16 {
        const OPEN_POLYLINE = 0;
        const CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M = 1;
        const CURVE_FIT = 2;
        const SPLINE_FIT = 4;
        const POLYLINE_3D = 8;
        const POLYGON_MESH_3D = 16;
        const CLOSED_POLYGON_MESH_IN_N = 32;
        const POLYFACE_MESH = 64;
        const CONTINUOUS_LINETYPE_PATTERN = 128;
    }
}

/// Smooth surface type for polylines.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum PolylineSmoothType {
    #[default]
    NoSmooth = 0,
    Quadratic = 5,
    Cubic = 6,
    Bezier = 8,
}

impl PolylineSmoothType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::NoSmooth,
            5 => Self::Quadratic,
            6 => Self::Cubic,
            8 => Self::Bezier,
            _ => return None,
        })
    }
}

/// Represents a DXF lightweight 2D polyline (`LWPOLYLINE`).
#[derive(Clone, Debug, PartialEq)]
pub struct Polyline2D {
    pub common: EntityCommon,
    pub vertexes: Vec<Polyline2DVertex>,
    pub is_closed: bool,
    pub elevation: f64,
    pub thickness: f64,
    pub linetype_generation: bool,
}

impl Polyline2D {
    pub fn new(vertexes: Vec<Polyline2DVertex>, is_closed: bool) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Polyline2D, DxfObjectCode::LW_POLYLINE),
            vertexes,
            is_closed,
            elevation: 0.0,
            thickness: 0.0,
            linetype_generation: false,
        }
    }

    pub fn from_points(points: impl IntoIterator<Item = Vector2>, is_closed: bool) -> Self {
        Self::new(
            points.into_iter().map(Polyline2DVertex::from_position).collect(),
            is_closed,
        )
    }

    pub fn set_constant_width(&mut self, width: f64) {
        let w = width.max(0.0);
        for v in &mut self.vertexes {
            v.start_width = w;
            v.end_width = w;
        }
    }

    pub fn reverse(&mut self) {
        let n = self.vertexes.len();
        if n < 2 {
            return;
        }
        let mut new_verts = Vec::with_capacity(n);
        for i in (0..n).rev() {
            let mut v = self.vertexes[i];
            let prev_idx = if i == 0 {
                if self.is_closed { n - 1 } else { 0 }
            } else {
                i - 1
            };
            v.bulge = if i == 0 && !self.is_closed {
                0.0
            } else {
                -self.vertexes[prev_idx].bulge
            };
            std::mem::swap(&mut v.start_width, &mut v.end_width);
            new_verts.push(v);
        }
        self.vertexes = new_verts;
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let new_normal = Vector3::normalize_finite_direction(transformation * self.common.normal)
            .unwrap_or(Vector3::UNIT_Z);
        let mut elev_sum = 0.0;
        let count = self.vertexes.len();
        for v in &mut self.vertexes {
            let wcs = transformation
                * MathHelper::transform_ocs_to_wcs(v.position, self.common.normal, self.elevation)
                + translation;
            let (ocs, elev) = MathHelper::transform_wcs_to_ocs(wcs, new_normal);
            v.position = ocs;
            elev_sum += elev;
        }
        self.common.normal = new_normal;
        if count > 0 {
            self.elevation = elev_sum / count as f64;
        }
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        BoundingRectangle::from_points(self.vertexes.iter().map(|v| {
            MathHelper::transform_ocs_to_wcs(v.position, self.common.normal, self.elevation)
                .to_vector2()
        }))
    }
}
