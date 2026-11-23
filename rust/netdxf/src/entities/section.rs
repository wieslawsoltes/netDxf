use crate::aci_color::AciColor;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

/// Represents a DXF `SECTION` (section plane) entity (`netDxf.Entities.Section`).
#[derive(Clone, Debug, PartialEq)]
pub struct Section {
    pub common: EntityCommon,
    pub state: i32,
    pub flags: i32,
    pub name: String,
    pub vertical_direction: Vector3,
    pub top_height: f64,
    pub bottom_height: f64,
    pub indicator_transparency: i16,
    pub indicator_color: AciColor,
    pub indicator_color_name: String,
    pub vertices: Vec<Vector3>,
    pub back_line_vertices: Vec<Vector3>,
    pub is_erased: bool,
    pub geometry_settings: Option<String>,
}

impl Section {
    pub const MAXIMUM_VERTICES: usize = 1_000_000;

    pub fn new(name: impl Into<String>, vertices: Vec<Vector3>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Section, "SECTION"),
            state: 1,
            flags: 0,
            name: name.into(),
            vertical_direction: Vector3::UNIT_Z,
            top_height: 1.0,
            bottom_height: -1.0,
            indicator_transparency: 70,
            indicator_color: AciColor::DEFAULT,
            indicator_color_name: String::new(),
            vertices,
            back_line_vertices: Vec::new(),
            is_erased: false,
            geometry_settings: None,
        }
    }

    #[inline]
    pub fn has_stored_geometry_settings(&self) -> bool {
        self.geometry_settings.is_some()
    }

    #[inline]
    pub fn stored_indicator_color(&self) -> AciColor {
        self.indicator_color
    }

    #[inline]
    pub fn stored_native_indicator_color(&self) -> Option<i32> {
        None
    }

    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        for v in &mut self.vertices {
            *v = transformation * *v + translation;
        }
        for v in &mut self.back_line_vertices {
            *v = transformation * *v + translation;
        }
        if let Some(d) = Vector3::normalize_finite_direction(transformation * self.vertical_direction)
        {
            self.vertical_direction = d;
        }
    }
}
