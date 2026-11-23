use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::tables::TextStyle;
use crate::vector3::Vector3;

/// MultiLeader content type (group code 172).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum MLeaderContentType {
    None = 0,
    Block = 1,
    #[default]
    MText = 2,
    Tolerance = 3,
}

impl MLeaderContentType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::None,
            1 => Self::Block,
            2 => Self::MText,
            3 => Self::Tolerance,
            _ => return None,
        })
    }
}

/// A single leader line polyline inside a `MultiLeader` leader node.
#[derive(Clone, Debug, PartialEq)]
pub struct MLeaderLeaderLine {
    pub points: Vec<Vector3>,
    pub index: i32,
}

/// A leader branch (dogleg + one or more leader lines) of a `MultiLeader`.
#[derive(Clone, Debug, PartialEq)]
pub struct MLeaderLeaderNode {
    pub has_last_leader_line_point: bool,
    pub has_dogleg_vector: bool,
    pub last_leader_line_point: Vector3,
    pub dogleg_vector: Vector3,
    pub dogleg_length: f64,
    pub index: i32,
    pub leader_lines: Vec<MLeaderLeaderLine>,
}

impl Default for MLeaderLeaderNode {
    fn default() -> Self {
        Self {
            has_last_leader_line_point: true,
            has_dogleg_vector: true,
            last_leader_line_point: Vector3::ZERO,
            dogleg_vector: Vector3::UNIT_X,
            dogleg_length: 8.0,
            index: 0,
            leader_lines: Vec::new(),
        }
    }
}

/// Represents a DXF `MULTILEADER` entity (`netDxf.Entities.MultiLeader`).
#[derive(Clone, Debug, PartialEq)]
pub struct MultiLeader {
    pub common: EntityCommon,
    pub style_name: String,
    pub style_handle: Option<String>,
    pub content_type: MLeaderContentType,
    pub content_scale: f64,
    pub content_base_point: Vector3,
    pub text: String,
    pub text_style: String,
    pub text_location: Vector3,
    pub text_direction: Vector3,
    pub text_height: f64,
    pub text_rotation: f64,
    pub block_name: Option<String>,
    pub block_position: Vector3,
    pub block_scale: Vector3,
    pub block_rotation: f64,
    pub arrowhead_size: f64,
    pub leaders: Vec<MLeaderLeaderNode>,
    pub version: i16,
    pub stored_version: i16,
}

impl MultiLeader {
    pub fn with_mtext(text: impl Into<String>, text_location: Vector3, leader_points: Vec<Vector3>) -> Self {
        let last_pt = leader_points.last().copied().unwrap_or(text_location);
        let node = MLeaderLeaderNode {
            last_leader_line_point: last_pt,
            leader_lines: vec![MLeaderLeaderLine {
                points: leader_points,
                index: 0,
            }],
            ..MLeaderLeaderNode::default()
        };
        Self {
            common: EntityCommon::new(EntityType::MultiLeader, DxfObjectCode::MULTILEADER),
            style_name: "Standard".to_string(),
            style_handle: None,
            content_type: MLeaderContentType::MText,
            content_scale: 1.0,
            content_base_point: text_location,
            text: text.into(),
            text_style: TextStyle::DEFAULT_NAME.to_string(),
            text_location,
            text_direction: Vector3::UNIT_X,
            text_height: 2.5,
            text_rotation: 0.0,
            block_name: None,
            block_position: Vector3::ZERO,
            block_scale: Vector3::new(1.0, 1.0, 1.0),
            block_rotation: 0.0,
            arrowhead_size: 2.5,
            leaders: vec![node],
            version: 2,
            stored_version: 2,
        }
    }

    pub fn context(&self) -> crate::entities::mleader_context::MLeaderContext {
        crate::entities::mleader_context::MLeaderContext {
            content_scale: self.content_scale,
            content_base_point: self.content_base_point,
            text_height: self.text_height,
            has_mtext: self.content_type == MLeaderContentType::MText,
            has_block: self.content_type == MLeaderContentType::Block,
            ..Default::default()
        }
    }

    pub fn properties(&self) -> crate::entities::mleader_context::MLeaderProperties {
        let mut props = crate::entities::mleader_context::MLeaderProperties::default();
        props.style_properties.content_type = self.content_type;
        props.style_properties.scale = self.content_scale;
        props.style_properties.arrow_size = self.arrowhead_size;
        props
    }

    pub fn validate(&self) -> Result<(), String> {
        if !self.content_scale.is_finite() || self.content_scale <= 0.0 {
            return Err("MultiLeader content_scale must be positive and finite.".into());
        }
        if !self.text_height.is_finite() || self.text_height <= 0.0 {
            return Err("MultiLeader text_height must be positive and finite.".into());
        }
        Ok(())
    }

    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        self.content_base_point = transformation * self.content_base_point + translation;
        self.text_location = transformation * self.text_location + translation;
        self.block_position = transformation * self.block_position + translation;
        let td = Vector3::normalize(transformation * self.text_direction);
        if !Vector3::is_nan(td) {
            self.text_direction = td;
        }
        let n = Vector3::normalize(transformation * self.common.normal);
        if !Vector3::is_nan(n) {
            self.common.normal = n;
        }
        for leader in &mut self.leaders {
            leader.last_leader_line_point =
                transformation * leader.last_leader_line_point + translation;
            let dv = Vector3::normalize(transformation * leader.dogleg_vector);
            if !Vector3::is_nan(dv) {
                leader.dogleg_vector = dv;
            }
            for line in &mut leader.leader_lines {
                for pt in &mut line.points {
                    *pt = transformation * *pt + translation;
                }
            }
        }
    }
}
