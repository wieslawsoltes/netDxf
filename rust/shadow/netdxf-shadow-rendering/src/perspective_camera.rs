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
    pub near_plane: f64,
    pub far_plane: f64,
}

impl Default for PerspectiveOptions {
    fn default() -> Self {
        Self {
            fov_degrees: 45.0,
            fov_mode: PerspectiveFieldOfView::Vertical,
            near_plane: 0.1,
            far_plane: 100_000.0,
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
    pub fn forward(self) -> Vec3 {
        (self.target - self.eye).normalized()
    }

    pub fn right(self) -> Vec3 {
        Vec3::cross(self.forward(), self.up).normalized()
    }

    pub fn camera_up(self) -> Vec3 {
        Vec3::cross(self.right(), self.forward()).normalized()
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

