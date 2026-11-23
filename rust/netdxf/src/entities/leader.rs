use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::tables::DimensionStyle;
use crate::vector2::Vector2;

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
    pub annotation_handle: Option<String>,
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
            annotation_handle: None,
        }
    }
}
