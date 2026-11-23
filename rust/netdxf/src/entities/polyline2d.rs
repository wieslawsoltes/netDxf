use bitflags::bitflags;

use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::arc::Arc;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::line::Line;
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
    pub legacy_default_start_width: f64,
    pub legacy_default_end_width: f64,
}

impl Polyline2D {
    pub const DEFAULT_SPLINE_SEGS: i16 = 8;

    pub fn new(vertexes: Vec<Polyline2DVertex>, is_closed: bool) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Polyline2D, DxfObjectCode::LW_POLYLINE),
            vertexes,
            is_closed,
            elevation: 0.0,
            thickness: 0.0,
            linetype_generation: false,
            legacy_default_start_width: 0.0,
            legacy_default_end_width: 0.0,
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

    #[inline]
    pub fn get_effective_start_width(&self, index: usize) -> f64 {
        self.vertexes.get(index).map_or(0.0, |v| {
            if v.start_width > 0.0 {
                v.start_width
            } else {
                self.legacy_default_start_width
            }
        })
    }

    #[inline]
    pub fn get_effective_end_width(&self, index: usize) -> f64 {
        self.vertexes.get(index).map_or(0.0, |v| {
            if v.end_width > 0.0 {
                v.end_width
            } else {
                self.legacy_default_end_width
            }
        })
    }

    pub fn set_vertex(&mut self, index: usize, vertex: Polyline2DVertex) -> bool {
        if let Some(slot) = self.vertexes.get_mut(index) {
            *slot = vertex;
            true
        } else {
            false
        }
    }

    pub fn set_vertex_bulge(&mut self, index: usize, bulge: f64) -> bool {
        if let Some(slot) = self.vertexes.get_mut(index) {
            slot.bulge = bulge;
            true
        } else {
            false
        }
    }

    pub fn set_vertex_widths(&mut self, index: usize, start_width: f64, end_width: f64) -> bool {
        if let Some(slot) = self.vertexes.get_mut(index) {
            slot.start_width = start_width.max(0.0);
            slot.end_width = end_width.max(0.0);
            true
        } else {
            false
        }
    }

    #[inline]
    pub fn smooth_type(&self) -> PolylineSmoothType {
        PolylineSmoothType::NoSmooth
    }

    #[inline]
    pub fn vertex_records(&self) -> &[Polyline2DVertex] {
        &self.vertexes
    }

    #[inline]
    pub fn end_sequence_record(&self) -> Option<&str> {
        None
    }

    /// Computes a polygonal approximation of the 2D polyline in OCS, tessellating bulge arcs
    /// with `precision` segments per arc.
    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector2> {
        let n = self.vertexes.len();
        if n == 0 {
            return Vec::new();
        }
        let segs = precision.max(2);
        let seg_count = if self.is_closed { n } else { n.saturating_sub(1) };
        let mut pts = Vec::new();
        for i in 0..seg_count {
            let v0 = self.vertexes[i];
            let v1 = self.vertexes[(i + 1) % n];
            pts.push(v0.position);
            if !MathHelper::is_zero(v0.bulge) {
                if let Some((center, radius, start_rad, sweep_rad)) =
                    bulge_to_arc(v0.position, v1.position, v0.bulge)
                {
                    let step = sweep_rad / segs as f64;
                    for s in 1..segs {
                        let a = start_rad + step * s as f64;
                        pts.push(Vector2::new(
                            center.x + radius * a.cos(),
                            center.y + radius * a.sin(),
                        ));
                    }
                }
            }
        }
        if !self.is_closed {
            pts.push(self.vertexes[n - 1].position);
        }
        pts
    }

    /// Explodes the polyline into individual `Line` and `Arc` entities in WCS.
    pub fn explode(&self) -> Vec<EntityObject> {
        let n = self.vertexes.len();
        if n < 2 {
            return Vec::new();
        }
        let seg_count = if self.is_closed { n } else { n - 1 };
        let mut out = Vec::with_capacity(seg_count);
        for i in 0..seg_count {
            let v0 = self.vertexes[i];
            let v1 = self.vertexes[(i + 1) % n];
            if MathHelper::is_zero(v0.bulge) {
                let p0 = MathHelper::transform_ocs_to_wcs(
                    v0.position,
                    self.common.normal,
                    self.elevation,
                );
                let p1 = MathHelper::transform_ocs_to_wcs(
                    v1.position,
                    self.common.normal,
                    self.elevation,
                );
                let mut line = Line::new(p0, p1);
                line.thickness = self.thickness;
                line.common.normal = self.common.normal;
                line.common.layer = self.common.layer.clone();
                line.common.color = self.common.color;
                line.common.linetype = self.common.linetype.clone();
                out.push(EntityObject::Line(Box::new(line)));
            } else if let Some((center_ocs, radius, start_rad, sweep_rad)) =
                bulge_to_arc(v0.position, v1.position, v0.bulge)
            {
                let (sa, ea) = if sweep_rad >= 0.0 {
                    (start_rad, start_rad + sweep_rad)
                } else {
                    (start_rad + sweep_rad, start_rad)
                };
                let center = MathHelper::transform_ocs_to_wcs(
                    center_ocs,
                    self.common.normal,
                    self.elevation,
                );
                let mut arc = Arc::new(
                    center,
                    radius,
                    MathHelper::normalize_angle(sa * MathHelper::RAD_TO_DEG),
                    MathHelper::normalize_angle(ea * MathHelper::RAD_TO_DEG),
                );
                arc.thickness = self.thickness;
                arc.common.normal = self.common.normal;
                arc.common.layer = self.common.layer.clone();
                arc.common.color = self.common.color;
                arc.common.linetype = self.common.linetype.clone();
                out.push(EntityObject::Arc(Box::new(arc)));
            }
        }
        out
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
                if self.is_closed {
                    n - 1
                } else {
                    0
                }
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

/// Converts a polyline segment `(p0, p1, bulge)` into `(center, radius, start_angle_rad, signed_sweep_rad)`.
fn bulge_to_arc(p0: Vector2, p1: Vector2, bulge: f64) -> Option<(Vector2, f64, f64, f64)> {
    let chord = p1 - p0;
    let dist = chord.modulus();
    if dist <= f64::EPSILON || bulge.abs() <= f64::EPSILON {
        return None;
    }
    let sweep = 4.0 * bulge.atan();
    let half_sweep = 0.5 * sweep;
    let sin_half = half_sweep.sin();
    if sin_half.abs() <= f64::EPSILON {
        return None;
    }
    let radius = (0.5 * dist / sin_half).abs();
    let mid = Vector2::new(0.5 * (p0.x + p1.x), 0.5 * (p0.y + p1.y));
    let perp = Vector2::new(-chord.y / dist, chord.x / dist);
    let offset = 0.5 * dist * (1.0 - bulge * bulge) / (2.0 * bulge);
    let center = mid + perp * offset;
    let start_rad = (p0.y - center.y).atan2(p0.x - center.x);
    Some((center, radius, start_rad, sweep))
}
