#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

// Camera, paper space, annotation, linetype, and viewport layer overrides
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum PerspectiveFieldOfView {
    #[default]
    Vertical,
    Horizontal,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PerspectiveOptions {
    pub fov_degrees: f64,
    pub fov_mode: PerspectiveFieldOfView,
    pub field_of_view: PerspectiveFieldOfView,
    pub film_height_millimeters: f64,
    pub near_plane: f64,
    pub far_plane: f64,
    pub near_distance_fraction: f64,
    pub maximum_points: usize,
    pub maximum_work: usize,
}

impl Default for PerspectiveOptions {
    fn default() -> Self {
        Self {
            fov_degrees: 45.0,
            fov_mode: PerspectiveFieldOfView::Vertical,
            field_of_view: PerspectiveFieldOfView::Vertical,
            film_height_millimeters: 24.0,
            near_plane: 0.1,
            far_plane: 100_000.0,
            near_distance_fraction: 0.01,
            maximum_points: 100_000,
            maximum_work: 500_000,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PickingRay {
    pub origin: Vec3,
    pub direction: Vec3,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PerspectiveCamera {
    pub eye: Vec3,
    pub target: Vec3,
    pub up: Vec3,
    pub options: PerspectiveOptions,
}

impl PerspectiveCamera {
    #[inline] pub fn center(self) -> Vec3 { self.target }
    #[inline] pub fn eye_distance(self) -> f64 { Vec3::distance(self.eye, self.target) }
    #[inline] pub fn maximum_depth(self) -> f64 { self.options.far_plane }
    #[inline] pub fn near_distance_fraction(self) -> f64 {
        let d = self.eye_distance().max(1e-9);
        (self.options.near_plane / d).clamp(1e-6, 0.99)
    }

    pub fn forward(self) -> Vec3 {
        (self.target - self.eye).normalized()
    }

    pub fn right(self) -> Vec3 {
        Vec3::cross(self.forward(), self.up).normalized()
    }

    pub fn camera_up(self) -> Vec3 {
        Vec3::cross(self.right(), self.forward()).normalized()
    }

    /// Transforms a world point into camera-target local coordinates `(right, up, depth)`.
    pub fn model_to_target(self, world: Vec3) -> Vec3 {
        let rel = world - self.target;
        Vec3::new(
            Vec3::dot(rel, self.right()),
            Vec3::dot(rel, self.camera_up()),
            Vec3::dot(rel, self.forward()),
        )
    }

    /// Transforms a camera-target local point back into world coordinates.
    pub fn target_to_model(self, local: Vec3) -> Vec3 {
        self.target
            + self.right() * local.x
            + self.camera_up() * local.y
            + self.forward() * local.z
    }

    pub fn try_project(self, world: Vec3, width: f64, height: f64) -> Option<Vec3> {
        self.project_point(world, width, height)
    }

    pub fn ray(self, screen_x: f64, screen_y: f64, width: f64, height: f64) -> PickingRay {
        self.unproject_ray(screen_x, screen_y, width, height)
    }

    /// Unprojects a screen coordinate `(screen_x, screen_y)` onto the plane perpendicular to the view at `target`.
    pub fn unproject(self, screen_x: f64, screen_y: f64, width: f64, height: f64) -> Option<Vec3> {
        let ray = self.unproject_ray(screen_x, screen_y, width, height);
        let fwd = self.forward();
        let denom = Vec3::dot(ray.direction, fwd);
        if denom.abs() < 1e-12 {
            return None;
        }
        let t = Vec3::dot(self.target - ray.origin, fwd) / denom;
        if t <= 0.0 {
            return None;
        }
        Some(ray.origin + ray.direction * t)
    }

    pub fn project_point(self, world: Vec3, width: f64, height: f64) -> Option<Vec3> {
        let rel = world - self.eye;
        let z = Vec3::dot(rel, self.forward());
        if z <= self.options.near_plane || z >= self.options.far_plane {
            return None;
        }
        let x = Vec3::dot(rel, self.right());
        let y = Vec3::dot(rel, self.camera_up());
        let aspect = (width / height.max(1.0)).max(1e-6);
        let half_tan = (self.options.fov_degrees.to_radians() * 0.5).tan();
        let (tan_x, tan_y) = match self.options.fov_mode {
            PerspectiveFieldOfView::Vertical => (half_tan * aspect, half_tan),
            PerspectiveFieldOfView::Horizontal => (half_tan, half_tan / aspect),
        };
        let ndc_x = x / (z * tan_x);
        let ndc_y = y / (z * tan_y);
        Some(Vec3::new(
            (ndc_x + 1.0) * 0.5 * width,
            (1.0 - ndc_y) * 0.5 * height,
            z,
        ))
    }

    pub fn unproject_ray(self, screen_x: f64, screen_y: f64, width: f64, height: f64) -> PickingRay {
        let aspect = (width / height.max(1.0)).max(1e-6);
        let half_tan = (self.options.fov_degrees.to_radians() * 0.5).tan();
        let (tan_x, tan_y) = match self.options.fov_mode {
            PerspectiveFieldOfView::Vertical => (half_tan * aspect, half_tan),
            PerspectiveFieldOfView::Horizontal => (half_tan, half_tan / aspect),
        };
        let ndc_x = (screen_x / width.max(1.0)) * 2.0 - 1.0;
        let ndc_y = 1.0 - (screen_y / height.max(1.0)) * 2.0;
        let dir = (self.forward()
            + self.right() * (ndc_x * tan_x)
            + self.camera_up() * (ndc_y * tan_y))
            .normalized();
        PickingRay {
            origin: self.eye,
            direction: dir,
        }
    }
}

