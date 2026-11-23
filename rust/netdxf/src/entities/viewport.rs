use bitflags::bitflags;

use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

bitflags! {
    /// Viewport status flags (group code 90).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct ViewportStatusFlags: i32 {
        const PERSPECTIVE_MODE = 1;
        const FRONT_CLIPPING = 2;
        const BACK_CLIPPING = 4;
        const UCS_FOLLOW = 8;
        const FRONT_CLIP_NOT_AT_EYE = 16;
        const UCS_ICON_VISIBILITY = 32;
        const UCS_ICON_AT_ORIGIN = 64;
        const FAST_ZOOM = 128;
        const SNAP_MODE = 256;
        const GRID_MODE = 512;
        const ISOMETRIC_SNAP_STYLE = 1024;
        const HIDE_PLOT_MODE = 2048;
        const ISO_PAIR_TOP = 4096;
        const ISO_PAIR_RIGHT = 8192;
        const DISPLAY_LOCKED = 16384;
        const VIEWPORT_ZOOM_LOCKING = 16384;
        const CURRENTLY_ALWAYS_ENABLED = 32768;
        const NON_RECTANGULAR_CLIPPING = 65536;
        const VIEWPORT_OFF = 131072;
        const DISPLAY_GRID_BEYOND_DRAWING_LIMITS = 262144;
        const ADAPTIVE_GRID_DISPLAY = 524288;
        const SUBDIVISION_GRID_BELOW_SPACING = 1048576;
    }
}

/// Represents a DXF paper-space `VIEWPORT` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Viewport {
    pub common: EntityCommon,
    pub center: Vector3,
    pub width: f64,
    pub height: f64,
    pub stacking: i16,
    pub id: i16,
    pub view_center: Vector2,
    pub snap_base_point: Vector2,
    pub snap_spacing: Vector2,
    pub grid_spacing: Vector2,
    pub view_direction: Vector3,
    pub view_target: Vector3,
    pub lens_length: f64,
    pub front_clip_plane: f64,
    pub back_clip_plane: f64,
    pub view_height: f64,
    pub snap_angle: f64,
    pub twist_angle: f64,
    pub circle_zoom_percent: i16,
    pub status: ViewportStatusFlags,
    pub frozen_layers: Vec<String>,
    pub elevation: f64,
    pub ucs_origin: Vector3,
    pub clipping_boundary: Option<String>,
    pub sun: Option<String>,
}

impl Viewport {
    pub fn new() -> Self {
        Self {
            common: EntityCommon::new(EntityType::Viewport, DxfObjectCode::VIEWPORT),
            center: Vector3::ZERO,
            width: 297.0,
            height: 210.0,
            stacking: 1,
            id: 1,
            view_center: Vector2::ZERO,
            snap_base_point: Vector2::ZERO,
            snap_spacing: Vector2::new(0.5, 0.5),
            grid_spacing: Vector2::new(10.0, 10.0),
            view_direction: Vector3::UNIT_Z,
            view_target: Vector3::ZERO,
            lens_length: 50.0,
            front_clip_plane: 0.0,
            back_clip_plane: 0.0,
            view_height: 210.0,
            snap_angle: 0.0,
            twist_angle: 0.0,
            circle_zoom_percent: 1000,
            status: ViewportStatusFlags::UCS_ICON_VISIBILITY
                | ViewportStatusFlags::FAST_ZOOM
                | ViewportStatusFlags::GRID_MODE,
            frozen_layers: Vec::new(),
            elevation: 0.0,
            ucs_origin: Vector3::ZERO,
            clipping_boundary: None,
            sun: None,
        }
    }

    #[inline] pub fn snap_base(&self) -> Vector2 { self.snap_base_point }

    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        self.center = transformation * self.center + translation;
        self.view_target = transformation * self.view_target + translation;
        if let Some(d) = Vector3::normalize_finite_direction(transformation * self.view_direction) {
            self.view_direction = d;
        }
    }
}

impl Default for Viewport {
    fn default() -> Self {
        Self::new()
    }
}
