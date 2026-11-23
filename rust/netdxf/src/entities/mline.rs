use bitflags::bitflags;

use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::line::Line;
use crate::entities::mline_justification::MLineJustification;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::objects::MLineStyle;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

bitflags! {
    /// MLine flags (group code 71).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct MLineFlags: i16 {
        const HAS = 1;
        const CLOSED = 2;
        const NO_START_CAPS = 4;
        const NO_END_CAPS = 8;
    }
}

/// Represents a vertex of an `MLine`.
#[derive(Clone, Debug, PartialEq)]
pub struct MLineVertex {
    pub position: Vector2,
    pub direction: Vector2,
    pub miter: Vector2,
    pub distances: Vec<Vec<f64>>,
}

impl MLineVertex {
    pub fn new(position: Vector2) -> Self {
        Self {
            position,
            direction: Vector2::UNIT_X,
            miter: Vector2::UNIT_Y,
            distances: Vec::new(),
        }
    }
}

/// Represents a DXF `MLINE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct MLine {
    pub common: EntityCommon,
    pub style: String,
    pub scale: f64,
    pub justification: MLineJustification,
    pub flags: MLineFlags,
    pub elevation: f64,
    pub vertexes: Vec<MLineVertex>,
}

impl MLine {
    pub fn new(vertexes: Vec<Vector2>) -> Self {
        let mut ml = Self {
            common: EntityCommon::new(EntityType::MLine, DxfObjectCode::MLINE),
            style: "Standard".to_string(),
            scale: 1.0,
            justification: MLineJustification::Top,
            flags: MLineFlags::HAS,
            elevation: 0.0,
            vertexes: vertexes.into_iter().map(MLineVertex::new).collect(),
        };
        ml.update(&MLineStyle::default_style());
        ml
    }

    #[inline]
    pub fn is_closed(&self) -> bool {
        self.flags.contains(MLineFlags::CLOSED)
    }

    pub fn set_is_closed(&mut self, closed: bool) {
        self.flags.set(MLineFlags::CLOSED, closed);
    }

    #[inline]
    pub fn no_start_caps(&self) -> bool {
        self.flags.contains(MLineFlags::NO_START_CAPS)
    }

    #[inline]
    pub fn no_end_caps(&self) -> bool {
        self.flags.contains(MLineFlags::NO_END_CAPS)
    }

    /// Recomputes vertex directions, miter bisectors, and element offset distances from `style`.
    pub fn update(&mut self, style: &MLineStyle) {
        let n = self.vertexes.len();
        if n < 2 {
            return;
        }
        let closed = self.is_closed();
        let mut dirs = Vec::with_capacity(n);
        for i in 0..n {
            let next = if i + 1 < n {
                self.vertexes[i + 1].position
            } else if closed {
                self.vertexes[0].position
            } else {
                self.vertexes[i].position + (self.vertexes[i].position - self.vertexes[i - 1].position)
            };
            let d = Vector2::normalize(next - self.vertexes[i].position);
            dirs.push(if d.modulus() > f64::EPSILON { d } else { Vector2::UNIT_X });
        }
        let max_off = style
            .elements
            .iter()
            .map(|e| e.offset)
            .fold(f64::NEG_INFINITY, f64::max);
        let min_off = style
            .elements
            .iter()
            .map(|e| e.offset)
            .fold(f64::INFINITY, f64::min);
        let just_shift = match self.justification {
            MLineJustification::Top => {
                if max_off.is_finite() { -max_off } else { 0.0 }
            }
            MLineJustification::Zero => 0.0,
            MLineJustification::Bottom => {
                if min_off.is_finite() { -min_off } else { 0.0 }
            }
        };

        for i in 0..n {
            let dir = dirs[i];
            let perp = Vector2::new(-dir.y, dir.x);
            let miter = if i == 0 && !closed {
                perp
            } else if i == n - 1 && !closed {
                let prev_d = dirs[i - 1];
                Vector2::new(-prev_d.y, prev_d.x)
            } else {
                let prev_d = dirs[(i + n - 1) % n];
                let p1 = Vector2::new(-prev_d.y, prev_d.x);
                let sum = p1 + perp;
                if sum.modulus() > 1e-9 {
                    Vector2::normalize(sum)
                } else {
                    perp
                }
            };
            self.vertexes[i].direction = dir;
            self.vertexes[i].miter = miter;
            let cos_half = Vector2::dot_product(miter, perp).abs().max(1e-6);
            self.vertexes[i].distances = style
                .elements
                .iter()
                .map(|el| vec![(el.offset + just_shift) * self.scale / cos_half, 0.0])
                .collect();
        }
    }

    /// Explodes the multiline into parallel `Line` entities for each element in `style`.
    pub fn explode(&self, style: &MLineStyle) -> Vec<EntityObject> {
        let n = self.vertexes.len();
        if n < 2 {
            return Vec::new();
        }
        let seg_count = if self.is_closed() { n } else { n - 1 };
        let mut out = Vec::new();
        for (el_idx, el) in style.elements.iter().enumerate() {
            for i in 0..seg_count {
                let v0 = &self.vertexes[i];
                let v1 = &self.vertexes[(i + 1) % n];
                let d0 = v0
                    .distances
                    .get(el_idx)
                    .and_then(|d| d.first().copied())
                    .unwrap_or(el.offset * self.scale);
                let d1 = v1
                    .distances
                    .get(el_idx)
                    .and_then(|d| d.first().copied())
                    .unwrap_or(el.offset * self.scale);
                let p0 = MathHelper::transform_ocs_to_wcs(
                    v0.position + v0.miter * d0,
                    self.common.normal,
                    self.elevation,
                );
                let p1 = MathHelper::transform_ocs_to_wcs(
                    v1.position + v1.miter * d1,
                    self.common.normal,
                    self.elevation,
                );
                let mut line = Line::new(p0, p1);
                line.common.layer = self.common.layer.clone();
                line.common.color = el.color;
                line.common.linetype = el.linetype.clone();
                out.push(EntityObject::Line(Box::new(line)));
            }
        }
        out
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
}
