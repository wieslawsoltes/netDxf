use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::tables::DimensionStyle;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Leader path type (group code 72).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum LeaderPathType {
    #[default]
    StraightLineSegments = 0,
    Spline = 1,
}

/// Represents a DXF `LEADER` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Leader {
    pub common: EntityCommon,
    pub style: String,
    pub show_arrowhead: bool,
    pub path_type: LeaderPathType,
    pub vertexes: Vec<Vector2>,
    pub elevation: f64,
    pub has_hookline: bool,
    pub line_color: AciColor,
    pub offset: Vector2,
    pub direction: Vector2,
    pub annotation_handle: Option<String>,
    pub style_overrides_list: Vec<crate::tables::DimensionStyleOverride>,
}

impl Leader {
    pub fn new(vertexes: Vec<Vector2>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Leader, DxfObjectCode::LEADER),
            style: DimensionStyle::DEFAULT_NAME.to_string(),
            show_arrowhead: true,
            path_type: LeaderPathType::StraightLineSegments,
            vertexes,
            elevation: 0.0,
            has_hookline: false,
            line_color: AciColor::BY_LAYER,
            offset: Vector2::ZERO,
            direction: Vector2::UNIT_X,
            annotation_handle: None,
            style_overrides_list: Vec::new(),
        }
    }

    pub fn update(&mut self, hook_length: f64) {
        if self.has_hookline && self.vertexes.len() >= 2 {
            let last = *self.vertexes.last().unwrap();
            let prev = self.vertexes[self.vertexes.len() - 2];
            let sign = if last.x >= prev.x { 1.0 } else { -1.0 };
            self.direction = Vector2::new(sign, 0.0);
            if hook_length > 0.0 {
                let hook_pt = Vector2::new(last.x + sign * hook_length, last.y);
                self.vertexes.push(hook_pt);
            }
        }
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let new_normal = Vector3::normalize(transformation * self.common.normal);
        if !Vector3::is_nan(new_normal) {
            self.common.normal = new_normal;
        }
        let mut new_elev = self.elevation;
        for v in &mut self.vertexes {
            let p = transformation * Vector3::new(v.x, v.y, self.elevation) + translation;
            v.x = p.x;
            v.y = p.y;
            new_elev = p.z;
        }
        self.elevation = new_elev;
        let dir3 = transformation * Vector3::new(self.direction.x, self.direction.y, 0.0);
        let dir2 = Vector2::normalize(Vector2::new(dir3.x, dir3.y));
        if !Vector2::is_nan(dir2) {
            self.direction = dir2;
        }
    }

    #[inline] pub fn annotation(&self) -> Option<&str> { self.annotation_handle.as_deref() }
    #[inline] pub fn hook(&self) -> Vector2 { self.direction }
    #[inline] pub fn normal(&self) -> Vector3 { self.common.normal }
    #[inline] pub fn style_overrides(&self) -> &[crate::tables::DimensionStyleOverride] { &self.style_overrides_list }
}
