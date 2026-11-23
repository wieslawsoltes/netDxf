use crate::aci_color::AciColor;
use crate::entities::MLeaderContentType;
use crate::vector3::Vector3;

/// A break segment along a multileader line.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderBreak {
    pub start: Vector3,
    pub end: Vector3,
}

/// Collection of breaks on a single multileader line.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderLineBreaks {
    pub index: i32,
    pub breaks: Vec<MLeaderBreak>,
}

/// A single leader line branch inside an [`MLeaderNode`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderLine {
    pub index: i32,
    pub vertices: Vec<Vector3>,
    pub breaks: Vec<MLeaderBreak>,
    pub line_type: Option<i16>,
    pub color: Option<AciColor>,
    pub lineweight: Option<i16>,
    pub arrow_head_handle: Option<String>,
    pub arrow_size: Option<f64>,
}

/// A leader node (dogleg + collection of leader lines) inside an [`MLeaderContext`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderNode {
    pub has_set_last_leader_line_point: bool,
    pub has_set_dogleg_vector: bool,
    pub last_leader_line_point: Vector3,
    pub dogleg_vector: Vector3,
    pub leader_branch_index: i32,
    pub dogleg_length: f64,
    pub lines: Vec<MLeaderLine>,
    pub breaks: Vec<MLeaderLineBreaks>,
}

/// MText content payload for a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderMTextContent {
    pub text: String,
    pub normal: Vector3,
    pub style_handle: Option<String>,
    pub location: Vector3,
    pub direction: Vector3,
    pub rotation: f64,
    pub width: f64,
    pub height: f64,
    pub line_spacing_factor: f64,
    pub line_spacing_style: i16,
    pub color: AciColor,
    pub alignment: i16,
    pub flow_direction: i16,
    pub background_fill_color: Option<AciColor>,
    pub background_scale_factor: f64,
    pub background_transparency: i32,
    pub use_background_fill: bool,
    pub use_background_mask_fill: bool,
    pub column_type: i16,
    pub text_height_automatic: bool,
    pub column_width: f64,
    pub column_gutter: f64,
    pub column_flow_reversed: bool,
    pub column_heights: Vec<f64>,
    pub word_break: bool,
}

impl Default for MLeaderMTextContent {
    fn default() -> Self {
        Self {
            text: String::new(),
            normal: Vector3::UNIT_Z,
            style_handle: None,
            location: Vector3::ZERO,
            direction: Vector3::UNIT_X,
            rotation: 0.0,
            width: 0.0,
            height: 0.18,
            line_spacing_factor: 1.0,
            line_spacing_style: 1,
            color: AciColor::BY_BLOCK,
            alignment: 1,
            flow_direction: 1,
            background_fill_color: None,
            background_scale_factor: 1.5,
            background_transparency: 0,
            use_background_fill: false,
            use_background_mask_fill: false,
            column_type: 0,
            text_height_automatic: false,
            column_width: 0.0,
            column_gutter: 0.0,
            column_flow_reversed: false,
            column_heights: Vec::new(),
            word_break: true,
        }
    }
}

/// Block content payload for a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderBlockContent {
    pub block_handle: Option<String>,
    pub normal: Vector3,
    pub location: Vector3,
    pub scale: Vector3,
    pub rotation: f64,
    pub color: AciColor,
    pub transform: [f64; 16],
}

impl Default for MLeaderBlockContent {
    fn default() -> Self {
        Self {
            block_handle: None,
            normal: Vector3::UNIT_Z,
            location: Vector3::ZERO,
            scale: Vector3::new(1.0, 1.0, 1.0),
            rotation: 0.0,
            color: AciColor::BY_BLOCK,
            transform: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
        }
    }
}

/// Arrowhead override entry on a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct MLeaderArrowHead {
    pub index: i32,
    pub block_handle: String,
}

/// Block attribute value entry on a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderBlockAttribute {
    pub attribute_definition_handle: String,
    pub index: i16,
    pub width: f64,
    pub text: String,
}

/// Geometric context (`CONTEXT_DATA`) of a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderContext {
    pub content_scale: f64,
    pub content_base_point: Vector3,
    pub text_height: f64,
    pub arrowhead_size: f64,
    pub landing_gap: f64,
    pub has_mtext: bool,
    pub mtext: Option<MLeaderMTextContent>,
    pub has_block: bool,
    pub block: Option<MLeaderBlockContent>,
    pub base_point: Vector3,
    pub base_direction: Vector3,
    pub base_vertical: Vector3,
    pub normal_reversed: bool,
    pub nodes: Vec<MLeaderNode>,
}

impl Default for MLeaderContext {
    fn default() -> Self {
        Self {
            content_scale: 1.0,
            content_base_point: Vector3::ZERO,
            text_height: 0.18,
            arrowhead_size: 0.18,
            landing_gap: 0.09,
            has_mtext: false,
            mtext: None,
            has_block: false,
            block: None,
            base_point: Vector3::ZERO,
            base_direction: Vector3::UNIT_X,
            base_vertical: Vector3::UNIT_Y,
            normal_reversed: false,
            nodes: Vec::new(),
        }
    }
}

/// Style properties shared by `MLEADERSTYLE` and `MULTILEADER`.
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderStyleProperties {
    pub content_type: MLeaderContentType,
    pub draw_mleader_order_type: i16,
    pub draw_leader_order_type: i16,
    pub max_leader_segment_points: i32,
    pub first_segment_angle_constraint: f64,
    pub second_segment_angle_constraint: f64,
    pub leader_line_type: i16,
    pub leader_line_color: AciColor,
    pub leader_line_weight: i32,
    pub enable_landing: bool,
    pub landing_gap: f64,
    pub enable_dogleg: bool,
    pub dogleg_length: f64,
    pub arrow_size: f64,
    pub text_height: f64,
    pub scale: f64,
}

impl Default for MLeaderStyleProperties {
    fn default() -> Self {
        Self {
            content_type: MLeaderContentType::MText,
            draw_mleader_order_type: 0,
            draw_leader_order_type: 1,
            max_leader_segment_points: 2,
            first_segment_angle_constraint: 0.0,
            second_segment_angle_constraint: 0.0,
            leader_line_type: 1,
            leader_line_color: AciColor::BY_BLOCK,
            leader_line_weight: -2,
            enable_landing: true,
            landing_gap: 0.09,
            enable_dogleg: true,
            dogleg_length: 0.36,
            arrow_size: 0.18,
            text_height: 0.18,
            scale: 1.0,
        }
    }
}

/// Entity-level property overrides on a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderProperties {
    pub style_properties: MLeaderStyleProperties,
    pub property_override_flag: i32,
    pub text_left_attachment_type: i16,
    pub text_right_attachment_type: i16,
    pub text_angle_type: i16,
    pub text_alignment_type: i16,
    pub block_connection_type: i16,
    pub enable_frame_text: bool,
    pub is_annotative: bool,
}

/// Complete structured data container for a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct MLeaderData {
    pub version: i16,
    pub context: MLeaderContext,
    pub properties: MLeaderProperties,
    pub arrow_heads: Vec<MLeaderArrowHead>,
    pub block_attributes: Vec<MLeaderBlockAttribute>,
}
