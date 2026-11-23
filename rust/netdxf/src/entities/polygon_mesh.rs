use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::face3d::Face3D;
use crate::entities::polyline2d::PolylineSmoothType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF `POLYLINE` 3D polygon mesh entity.
#[derive(Clone, Debug, PartialEq)]
pub struct PolygonMesh {
    pub common: EntityCommon,
    pub m: i16,
    pub n: i16,
    pub vertexes: Vec<Vector3>,
    pub is_closed_in_m: bool,
    pub is_closed_in_n: bool,
    pub smooth_type: PolylineSmoothType,
    pub density_m: i16,
    pub density_n: i16,
}

impl PolygonMesh {
    pub const DEFAULT_SURF_U: i16 = 6;
    pub const DEFAULT_SURF_V: i16 = 6;
    pub const MAXIMUM_BEZIER_BLEND_OPERATIONS: usize = 1_000_000;

    pub fn new(m: i16, n: i16, vertexes: Vec<Vector3>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::PolygonMesh, DxfObjectCode::POLYLINE),
            m,
            n,
            vertexes,
            is_closed_in_m: false,
            is_closed_in_n: false,
            smooth_type: PolylineSmoothType::NoSmooth,
            density_m: Self::DEFAULT_SURF_U,
            density_n: Self::DEFAULT_SURF_V,
        }
    }

    #[inline]
    pub fn is_closed_in_u(&self) -> bool {
        self.is_closed_in_m
    }

    #[inline]
    pub fn is_closed_in_v(&self) -> bool {
        self.is_closed_in_n
    }

    #[inline]
    pub fn density_u(&self) -> i16 {
        self.density_m
    }

    #[inline]
    pub fn density_v(&self) -> i16 {
        self.density_n
    }

    #[inline]
    pub fn get_vertex(&self, u: usize, v: usize) -> Option<Vector3> {
        let m = self.m.max(0) as usize;
        let n = self.n.max(0) as usize;
        if u < m && v < n {
            self.vertexes.get(u * n + v).copied()
        } else {
            None
        }
    }

    pub fn set_vertex(&mut self, u: usize, v: usize, point: Vector3) -> bool {
        let m = self.m.max(0) as usize;
        let n = self.n.max(0) as usize;
        if u < m && v < n {
            if let Some(slot) = self.vertexes.get_mut(u * n + v) {
                *slot = point;
                return true;
            }
        }
        false
    }

    /// Explodes the polygon mesh grid into `Face3D` quads.
    pub fn explode(&self) -> Vec<EntityObject> {
        let m = self.m.max(0) as usize;
        let n = self.n.max(0) as usize;
        if m < 2 || n < 2 || self.vertexes.len() < m * n {
            return Vec::new();
        }
        let u_max = if self.is_closed_in_m { m } else { m - 1 };
        let v_max = if self.is_closed_in_n { n } else { n - 1 };
        let mut out = Vec::with_capacity(u_max * v_max);
        for u in 0..u_max {
            let u1 = (u + 1) % m;
            for v in 0..v_max {
                let v1 = (v + 1) % n;
                let p00 = self.vertexes[u * n + v];
                let p10 = self.vertexes[u1 * n + v];
                let p11 = self.vertexes[u1 * n + v1];
                let p01 = self.vertexes[u * n + v1];
                let mut face = Face3D::new(p00, p10, p11, p01);
                face.common.layer = self.common.layer.clone();
                face.common.color = self.common.color;
                out.push(EntityObject::Face3D(Box::new(face)));
            }
        }
        out
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
