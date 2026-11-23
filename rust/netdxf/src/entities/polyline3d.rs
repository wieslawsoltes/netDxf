use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::line::Line;
use crate::entities::polyline2d::PolylineSmoothType;
use crate::entities::spline::Spline;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF 3D polyline (`POLYLINE` + `VERTEX` + `SEQEND`).
#[derive(Clone, Debug, PartialEq)]
pub struct Polyline3D {
    pub common: EntityCommon,
    pub vertexes: Vec<Vector3>,
    pub is_closed: bool,
    pub smooth_type: PolylineSmoothType,
    pub linetype_generation: bool,
}

impl Polyline3D {
    pub const DEFAULT_SPLINE_SEGS: i16 = 8;
    pub const MAXIMUM_CONVERTED_VERTICES: usize = 1_000_000;
    pub const MAXIMUM_EXPLODED_SEGMENTS: usize = 1_000_000;

    pub fn new(vertexes: Vec<Vector3>, is_closed: bool) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Polyline3D, DxfObjectCode::POLYLINE),
            vertexes,
            is_closed,
            smooth_type: PolylineSmoothType::NoSmooth,
            linetype_generation: false,
        }
    }

    pub fn insert_vertex(&mut self, index: usize, vertex: Vector3) {
        let idx = index.min(self.vertexes.len());
        self.vertexes.insert(idx, vertex);
    }

    pub fn move_vertex(&mut self, index: usize, delta: Vector3) -> bool {
        if let Some(v) = self.vertexes.get_mut(index) {
            *v += delta;
            true
        } else {
            false
        }
    }

    pub fn set_vertex(&mut self, index: usize, vertex: Vector3) -> bool {
        if let Some(v) = self.vertexes.get_mut(index) {
            *v = vertex;
            true
        } else {
            false
        }
    }

    pub fn remove_vertex_at(&mut self, index: usize) -> Option<Vector3> {
        if index < self.vertexes.len() {
            Some(self.vertexes.remove(index))
        } else {
            None
        }
    }

    /// Computes polygonal vertices, applying quadratic/cubic/bezier smoothing when `smooth_type` is set.
    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector3> {
        if self.vertexes.len() < 2 || self.smooth_type == PolylineSmoothType::NoSmooth {
            return self.vertexes.clone();
        }
        let degree = match self.smooth_type {
            PolylineSmoothType::Quadratic => 2,
            PolylineSmoothType::Cubic | PolylineSmoothType::Bezier => 3,
            PolylineSmoothType::NoSmooth => 1,
        };
        if self.vertexes.len() <= degree as usize {
            return self.vertexes.clone();
        }
        let mut pts = self.vertexes.clone();
        if self.is_closed {
            pts.push(self.vertexes[0]);
        }
        let spline = Spline::from_control_points(pts, degree, false);
        let samples = (precision.max(2) * self.vertexes.len()).min(Self::MAXIMUM_CONVERTED_VERTICES);
        spline.polygonal_vertexes(samples)
    }

    /// Explodes the 3D polyline into individual `Line` entities.
    pub fn explode(&self) -> Vec<EntityObject> {
        let pts = if self.smooth_type == PolylineSmoothType::NoSmooth {
            self.vertexes.clone()
        } else {
            self.polygonal_vertexes(Self::DEFAULT_SPLINE_SEGS as usize)
        };
        let n = pts.len();
        if n < 2 {
            return Vec::new();
        }
        let segs = if self.is_closed && self.smooth_type == PolylineSmoothType::NoSmooth {
            n
        } else {
            n - 1
        }
        .min(Self::MAXIMUM_EXPLODED_SEGMENTS);
        let mut out = Vec::with_capacity(segs);
        for i in 0..segs {
            let mut line = Line::new(pts[i], pts[(i + 1) % n]);
            line.common.layer = self.common.layer.clone();
            line.common.color = self.common.color;
            line.common.linetype = self.common.linetype.clone();
            out.push(EntityObject::Line(Box::new(line)));
        }
        out
    }

    pub fn reverse(&mut self) {
        self.vertexes.reverse();
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        for v in &mut self.vertexes {
            *v = transformation * *v + translation;
        }
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        BoundingRectangle::from_points(self.vertexes.iter().map(|v| v.to_vector2()))
    }
}
