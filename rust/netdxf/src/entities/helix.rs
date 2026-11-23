use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::spline::Spline;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Helix geometric constraint mode (DXF group 280).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum HelixConstraint {
    TurnHeight = 0,
    #[default]
    Turns = 1,
    Height = 2,
}

impl HelixConstraint {
    pub fn from_i16(v: i16) -> Self {
        match v {
            0 => Self::TurnHeight,
            2 => Self::Height,
            _ => Self::Turns,
        }
    }
}

/// Represents a DXF `HELIX` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Helix {
    pub common: EntityCommon,
    pub axis_base_point: Vector3,
    pub start_point: Vector3,
    pub axis_vector: Vector3,
    pub radius: f64,
    pub turns: f64,
    pub turn_height: f64,
    pub handedness: bool,
    pub constrain: i16,
    pub major_release_number: i32,
    pub maintenance_release_number: i32,
}

impl Helix {
    pub fn new(
        axis_base_point: Vector3,
        start_point: Vector3,
        turns: f64,
        turn_height: f64,
    ) -> Self {
        let radius = Vector3::distance(axis_base_point, start_point);
        Self {
            common: EntityCommon::new(EntityType::Helix, DxfObjectCode::HELIX),
            axis_base_point,
            start_point,
            axis_vector: Vector3::UNIT_Z,
            radius,
            turns,
            turn_height,
            handedness: true,
            constrain: HelixConstraint::Turns as i16,
            major_release_number: 29,
            maintenance_release_number: 63,
        }
    }

    /// Creates a helix with explicit axis vector, constraint, and handedness.
    pub fn create(
        axis_base_point: Vector3,
        start_point: Vector3,
        axis_vector: Vector3,
        turns: f64,
        turn_height: f64,
        is_right_handed: bool,
        constraint: HelixConstraint,
    ) -> Self {
        let mut h = Self::new(axis_base_point, start_point, turns, turn_height);
        h.axis_vector =
            Vector3::normalize_finite_direction(axis_vector).unwrap_or(Vector3::UNIT_Z);
        h.handedness = is_right_handed;
        h.constrain = constraint as i16;
        h
    }

    #[inline]
    pub fn constraint(&self) -> HelixConstraint {
        HelixConstraint::from_i16(self.constrain)
    }

    #[inline]
    pub fn set_constraint(&mut self, constraint: HelixConstraint) {
        self.constrain = constraint as i16;
    }

    #[inline]
    pub fn is_right_handed(&self) -> bool {
        self.handedness
    }

    #[inline]
    pub fn set_is_right_handed(&mut self, right_handed: bool) {
        self.handedness = right_handed;
    }

    /// Evaluates the orthonormal basis `(axis, radial_x, radial_y)` and base radius.
    fn local_frame(&self) -> (Vector3, Vector3, Vector3, f64) {
        let axis = Vector3::normalize_finite_direction(self.axis_vector).unwrap_or(Vector3::UNIT_Z);
        let rel = self.start_point - self.axis_base_point;
        let radial = rel - axis * Vector3::dot_product(rel, axis);
        let r0 = radial.modulus();
        let x_dir = Vector3::normalize_finite_direction(radial).unwrap_or_else(|| {
            let fallback = if axis.x.abs() < 0.9 {
                Vector3::UNIT_X
            } else {
                Vector3::UNIT_Y
            };
            Vector3::normalize(Vector3::cross_product(axis, fallback))
        });
        let y_dir = if self.handedness {
            Vector3::cross_product(axis, x_dir)
        } else {
            Vector3::cross_product(x_dir, axis)
        };
        (axis, x_dir, y_dir, r0)
    }

    /// Evaluates the analytical helix definition at normalized parameter `u` in `[0, 1]`.
    pub fn evaluate_definition(&self, u: f64) -> Vector3 {
        let (axis, x_dir, y_dir, r0) = self.local_frame();
        let theta = 2.0 * std::f64::consts::PI * self.turns * u;
        let r = r0 + (self.radius - r0) * u;
        let h = self.turns * self.turn_height * u;
        let (sin_t, cos_t) = theta.sin_cos();
        self.axis_base_point + x_dir * (r * cos_t) + y_dir * (r * sin_t) + axis * h
    }

    /// Evaluates the first derivative `dP/du` of the analytical helix definition at `u` in `[0, 1]`.
    pub fn evaluate_definition_derivative(&self, u: f64) -> Vector3 {
        let (axis, x_dir, y_dir, r0) = self.local_frame();
        let omega = 2.0 * std::f64::consts::PI * self.turns;
        let theta = omega * u;
        let dr = self.radius - r0;
        let r = r0 + dr * u;
        let dh = self.turns * self.turn_height;
        let (sin_t, cos_t) = theta.sin_cos();
        let dx = dr * cos_t - r * omega * sin_t;
        let dy = dr * sin_t + r * omega * cos_t;
        x_dir * dx + y_dir * dy + axis * dh
    }

    /// Estimates the chord-to-curve approximation error bound for a cubic spline with `segments`.
    pub fn get_approximation_error_bound(&self, segments: usize) -> f64 {
        let segs = segments.max(1) as f64;
        let total_angle = 2.0 * std::f64::consts::PI * self.turns.abs();
        let dtheta = total_angle / segs;
        let max_r = self.radius.abs().max(Vector3::distance(self.axis_base_point, self.start_point));
        max_r * (1.0 - (0.5 * dtheta).cos()).max(0.0)
    }

    /// Converts the helix to an approximating cubic `Spline`.
    pub fn to_spline(&self, segments: usize) -> Spline {
        let segs = segments.max((self.turns.abs().ceil() as usize * 8).max(8));
        let mut pts = Vec::with_capacity(segs + 1);
        for i in 0..=segs {
            let u = i as f64 / segs as f64;
            pts.push(self.evaluate_definition(u));
        }
        let mut spline = Spline::from_control_points(pts, 3, false);
        spline.common.layer = self.common.layer.clone();
        spline.common.color = self.common.color;
        spline.common.linetype = self.common.linetype.clone();
        spline
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.axis_base_point = transformation * self.axis_base_point + translation;
        self.start_point = transformation * self.start_point + translation;
        let transformed_axis = transformation * self.axis_vector;
        let axis_scale = transformed_axis.modulus();
        if let Some(n) = Vector3::normalize_finite_direction(transformed_axis) {
            self.axis_vector = n;
        }
        if axis_scale > f64::EPSILON {
            self.turn_height *= axis_scale;
        }
        let det = transformation.determinant();
        if det < 0.0 {
            self.handedness = !self.handedness;
        }
        self.radius = Vector3::distance(self.axis_base_point, self.start_point);
    }
}
