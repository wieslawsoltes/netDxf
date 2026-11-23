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

impl MLeaderLine {
    #[inline]
    pub fn stored_color(&self) -> Option<AciColor> {
        self.color
    }
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
    pub attachment_direction: i16,
    pub lines: Vec<MLeaderLine>,
    pub breaks: Vec<MLeaderLineBreaks>,
}

impl MLeaderNode {
    #[inline] pub fn index(&self) -> i32 { self.leader_branch_index }
    #[inline] pub fn has_last_leader_point(&self) -> bool { self.has_set_last_leader_line_point }
    #[inline] pub fn has_dogleg_vector(&self) -> bool { self.has_set_dogleg_vector }
    #[inline] pub fn last_leader_point(&self) -> Vector3 { self.last_leader_line_point }
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

impl MLeaderMTextContent {
    #[inline] pub fn position(&self) -> Vector3 { self.location }
    #[inline] pub fn defined_height(&self) -> f64 { self.height }
    #[inline] pub fn style(&self) -> Option<&str> { self.style_handle.as_deref() }
    #[inline] pub fn attachment(&self) -> i16 { self.alignment }
    #[inline] pub fn auto_height(&self) -> bool { self.text_height_automatic }
    #[inline] pub fn background_color(&self) -> Option<AciColor> { self.background_fill_color }
    #[inline] pub fn background_scale(&self) -> f64 { self.background_scale_factor }
    #[inline] pub fn has_background_fill(&self) -> bool { self.use_background_fill }
    #[inline] pub fn use_window_background_color(&self) -> bool { self.use_background_mask_fill }
    #[inline] pub fn use_word_break(&self) -> bool { self.word_break }
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

impl MLeaderBlockContent {
    #[inline] pub fn block(&self) -> Option<&str> { self.block_handle.as_deref() }
    #[inline] pub fn position(&self) -> Vector3 { self.location }
    #[inline] pub fn transformation_matrix(&self) -> &[f64; 16] { &self.transform }
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

impl MLeaderArrowHead {
    #[inline]
    pub fn block(&self) -> &str {
        &self.block_handle
    }
}

/// Block attribute value entry on a [`MultiLeader`](crate::entities::MultiLeader).
#[derive(Debug, Clone, PartialEq)]
pub struct MLeaderBlockAttribute {
    pub attribute_definition_handle: String,
    pub index: i16,
    pub width: f64,
    pub text: String,
}

impl MLeaderBlockAttribute {
    #[inline]
    pub fn definition(&self) -> &str {
        &self.attribute_definition_handle
    }
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
    pub text_left_attachment: i16,
    pub text_right_attachment: i16,
    pub text_alignment: i16,
    pub block_connection_type: i16,
    pub top_attachment: i16,
    pub bottom_attachment: i16,
}

impl MLeaderContext {
    #[inline] pub fn scale(&self) -> f64 { self.content_scale }
    #[inline] pub fn plane_origin(&self) -> Vector3 { self.base_point }
    #[inline] pub fn plane_x_axis(&self) -> Vector3 { self.base_direction }
    #[inline] pub fn plane_y_axis(&self) -> Vector3 { self.base_vertical }
    #[inline] pub fn plane_normal_reversed(&self) -> bool { self.normal_reversed }
    #[inline] pub fn leaders(&self) -> &[MLeaderNode] { &self.nodes }
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
            text_left_attachment: 1,
            text_right_attachment: 1,
            text_alignment: 0,
            block_connection_type: 0,
            top_attachment: 9,
            bottom_attachment: 9,
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
    pub leader_line_type_name: String,
    pub leader_line_weight: i32,
    pub enable_landing: bool,
    pub landing_gap: f64,
    pub enable_dogleg: bool,
    pub dogleg_length: f64,
    pub arrow_head: Option<String>,
    pub arrow_size: f64,
    pub default_text: String,
    pub text_style: String,
    pub text_left_attachment: i16,
    pub text_right_attachment: i16,
    pub text_top_attachment: i16,
    pub text_bottom_attachment: i16,
    pub text_attachment_direction: i16,
    pub text_angle_type: i16,
    pub text_alignment: i16,
    pub text_color: AciColor,
    pub text_height: f64,
    pub enable_frame_text: bool,
    pub text_always_left_justify: bool,
    pub align_space: f64,
    pub block: Option<String>,
    pub block_color: AciColor,
    pub block_scale_x: f64,
    pub block_scale_y: f64,
    pub block_scale_z: f64,
    pub enable_block_scale: bool,
    pub block_rotation: f64,
    pub enable_block_rotation: bool,
    pub block_connection_type: i16,
    pub scale: f64,
    pub break_size: f64,
    pub extend_leader_to_text: bool,
    pub is_annotative: bool,
}

impl MLeaderStyleProperties {
    #[inline] pub fn arrow_head_size(&self) -> f64 { self.arrow_size }
    #[inline] pub fn break_gap(&self) -> f64 { self.break_size }
    #[inline] pub fn description(&self) -> &str { &self.default_text }
    #[inline] pub fn draw_leader_order(&self) -> i16 { self.draw_leader_order_type }
    #[inline] pub fn draw_mleader_order(&self) -> i16 { self.draw_mleader_order_type }
    #[inline] pub fn first_segment_angle(&self) -> f64 { self.first_segment_angle_constraint }
    #[inline] pub fn second_segment_angle(&self) -> f64 { self.second_segment_angle_constraint }
    #[inline] pub fn has_block_rotation(&self) -> bool { self.enable_block_rotation }
    #[inline] pub fn has_block_scaling(&self) -> bool { self.enable_block_scale }
    #[inline] pub fn has_dogleg(&self) -> bool { self.enable_dogleg }
    #[inline] pub fn has_landing(&self) -> bool { self.enable_landing }
    #[inline] pub fn has_text_frame(&self) -> bool { self.enable_frame_text }
    #[inline] pub fn leader_line_linetype(&self) -> &str { &self.leader_line_type_name }
    #[inline] pub fn leader_line_lineweight(&self) -> i32 { self.leader_line_weight }
    #[inline] pub fn max_leader_points(&self) -> i32 { self.max_leader_segment_points }
    #[inline] pub fn maximum_leader_points(&self) -> i32 { self.max_leader_segment_points }
    #[inline] pub fn leader_type(&self) -> i16 { self.leader_line_type }
    #[inline] pub fn overwrite_property_value(&self) -> bool { false }
    #[inline] pub fn text_align_always_left(&self) -> bool { self.text_always_left_justify }
    #[inline] pub fn text_alignment_type(&self) -> i16 { self.text_alignment }
    #[inline] pub fn text_always_left(&self) -> bool { self.text_always_left_justify }
    #[inline] pub fn block_scale(&self) -> Vector3 {
        Vector3::new(self.block_scale_x, self.block_scale_y, self.block_scale_z)
    }
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
            leader_line_type_name: "ByBlock".to_string(),
            leader_line_weight: -2,
            enable_landing: true,
            landing_gap: 0.09,
            enable_dogleg: true,
            dogleg_length: 0.36,
            arrow_head: None,
            arrow_size: 0.18,
            default_text: String::new(),
            text_style: "Standard".to_string(),
            text_left_attachment: 1,
            text_right_attachment: 1,
            text_top_attachment: 9,
            text_bottom_attachment: 9,
            text_attachment_direction: 0,
            text_angle_type: 1,
            text_alignment: 0,
            text_color: AciColor::BY_BLOCK,
            text_height: 0.18,
            enable_frame_text: false,
            text_always_left_justify: false,
            align_space: 0.18,
            block: None,
            block_color: AciColor::BY_BLOCK,
            block_scale_x: 1.0,
            block_scale_y: 1.0,
            block_scale_z: 1.0,
            enable_block_scale: true,
            block_rotation: 0.0,
            enable_block_rotation: true,
            block_connection_type: 0,
            scale: 1.0,
            break_size: 0.125,
            extend_leader_to_text: false,
            is_annotative: false,
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
    pub arrow_heads: Vec<MLeaderArrowHead>,
    pub block_attributes: Vec<MLeaderBlockAttribute>,
}

impl MLeaderProperties {
    #[inline] pub fn content_type(&self) -> MLeaderContentType { self.style_properties.content_type }
    #[inline] pub fn arrow_head(&self) -> Option<&str> { self.style_properties.arrow_head.as_deref() }
    #[inline] pub fn arrow_head_size(&self) -> f64 { self.style_properties.arrow_size }
    #[inline] pub fn block(&self) -> Option<&str> { self.style_properties.block.as_deref() }
    #[inline] pub fn block_color(&self) -> AciColor { self.style_properties.block_color }
    #[inline] pub fn block_rotation(&self) -> f64 { self.style_properties.block_rotation }
    #[inline] pub fn block_scale(&self) -> Vector3 {
        Vector3::new(
            self.style_properties.block_scale_x,
            self.style_properties.block_scale_y,
            self.style_properties.block_scale_z,
        )
    }
    #[inline] pub fn dogleg_length(&self) -> f64 { self.style_properties.dogleg_length }
    #[inline] pub fn enable_dogleg(&self) -> bool { self.style_properties.enable_dogleg }
    #[inline] pub fn enable_landing(&self) -> bool { self.style_properties.enable_landing }
    #[inline] pub fn landing_gap(&self) -> f64 { self.style_properties.landing_gap }
    #[inline] pub fn leader_line_color(&self) -> AciColor { self.style_properties.leader_line_color }
    #[inline] pub fn leader_line_type(&self) -> i16 { self.style_properties.leader_line_type }
    #[inline] pub fn leader_line_weight(&self) -> i32 { self.style_properties.leader_line_weight }
    #[inline] pub fn scale(&self) -> f64 { self.style_properties.scale }
    #[inline] pub fn text_color(&self) -> AciColor { self.style_properties.text_color }
    #[inline] pub fn text_height(&self) -> f64 { self.style_properties.text_height }
    #[inline] pub fn text_style(&self) -> &str { &self.style_properties.text_style }
    #[inline] pub fn has_dogleg(&self) -> bool { self.style_properties.enable_dogleg }
    #[inline] pub fn has_landing(&self) -> bool { self.style_properties.enable_landing }
    #[inline] pub fn has_text_frame(&self) -> bool { self.enable_frame_text }
    #[inline] pub fn is_text_direction_negative(&self) -> bool { false }
    #[inline] pub fn leader_extend_to_text(&self) -> bool { self.style_properties.extend_leader_to_text }
    #[inline] pub fn leader_type(&self) -> i16 { self.style_properties.leader_line_type }
    #[inline] pub fn property_override_flags(&self) -> i32 { self.property_override_flag }
    #[inline] pub fn style(&self) -> &str { &self.style_properties.text_style }
    #[inline] pub fn text_attachment_direction(&self) -> i16 { self.style_properties.text_attachment_direction }
    #[inline] pub fn text_attachment_point(&self) -> i16 { self.text_left_attachment_type }
    #[inline] pub fn text_bottom_attachment(&self) -> i16 { self.style_properties.text_bottom_attachment }
    #[inline] pub fn text_left_attachment(&self) -> i16 { self.text_left_attachment_type }
    #[inline] pub fn text_right_attachment(&self) -> i16 { self.text_right_attachment_type }
    #[inline] pub fn text_top_attachment(&self) -> i16 { self.style_properties.text_top_attachment }
    #[inline] pub fn leader_line_linetype(&self) -> &str { &self.style_properties.leader_line_type_name }
    #[inline] pub fn leader_line_lineweight(&self) -> i32 { self.style_properties.leader_line_weight }
    #[inline] pub fn text_editor_alignment(&self) -> i16 { self.text_alignment_type }
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
