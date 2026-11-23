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
    pub vertices: Vec<Vector3>,
    pub back_line_vertices: Vec<Vector3>,
}

impl Section {
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
            vertices,
            back_line_vertices: Vec::new(),
        }
    }
}
