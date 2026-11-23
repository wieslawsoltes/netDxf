use crate::aci_color::AciColor;
use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::attribute_definition::AttributeDefinition;
use crate::entities::attribute_flags::AttributeFlags;
use crate::entities::text_alignment::TextAlignment;
use crate::lineweight::Lineweight;
use crate::tables::{Layer, Linetype, TextStyle};
use crate::transparency::Transparency;
use crate::vector3::Vector3;

/// Represents an attribute instance attached to an `Insert` (`ATTRIB`).
#[derive(Clone, Debug, PartialEq)]
pub struct Attribute {
    pub object: DxfObject,
    pub tag: String,
    pub value: String,
    pub style: String,
    pub position: Vector3,
    pub flags: AttributeFlags,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub rotation: f64,
    pub alignment: TextAlignment,
    pub is_backward: bool,
    pub is_upside_down: bool,
    pub layer: String,
    pub color: AciColor,
    pub linetype: String,
    pub lineweight: Lineweight,
    pub transparency: Transparency,
    pub normal: Vector3,
    pub color_name: String,
    pub is_position_locked: bool,
    pub mtext: Option<crate::entities::AttributeMText>,
}

impl Attribute {
    pub fn new(tag: impl Into<String>, value: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::ATTRIBUTE),
            tag: tag.into(),
            value: value.into(),
            style: TextStyle::DEFAULT_NAME.to_string(),
            position: Vector3::ZERO,
            flags: AttributeFlags::VISIBLE,
            height: 1.0,
            width_factor: 1.0,
            oblique_angle: 0.0,
            rotation: 0.0,
            alignment: TextAlignment::BaselineLeft,
            is_backward: false,
            is_upside_down: false,
            layer: Layer::DEFAULT_NAME.to_string(),
            color: AciColor::BY_LAYER,
            linetype: Linetype::BY_LAYER_NAME.to_string(),
            lineweight: Lineweight::ByLayer,
            transparency: Transparency::BY_LAYER,
            normal: Vector3::UNIT_Z,
            color_name: String::new(),
            is_position_locked: false,
            mtext: None,
        }
    }

    pub fn from_definition(def: &AttributeDefinition) -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::ATTRIBUTE),
            tag: def.tag.clone(),
            value: def.value.clone(),
            style: def.style.clone(),
            position: def.position,
            flags: def.flags,
            height: def.height,
            width_factor: def.width_factor,
            oblique_angle: def.oblique_angle,
            rotation: def.rotation,
            alignment: def.alignment,
            is_backward: def.is_backward,
            is_upside_down: def.is_upside_down,
            layer: def.layer.clone(),
            color: def.color,
            linetype: def.linetype.clone(),
            lineweight: def.lineweight,
            transparency: def.transparency,
            normal: def.normal,
            color_name: def.color_name.clone(),
            is_position_locked: def.is_position_locked,
            mtext: def.mtext.clone(),
        }
    }

    #[inline] pub fn is_visible(&self) -> bool { !self.flags.contains(AttributeFlags::HIDDEN) }
    #[inline] pub fn has_mtext(&self) -> bool { self.mtext.is_some() }
    #[inline] pub fn get_mtext(&self) -> Option<&crate::entities::AttributeMText> { self.mtext.as_ref() }
    pub fn get_text_data(&self) -> crate::entities::AttributeTextData {
        crate::entities::AttributeTextData {
            text: self.value.clone(),
            content: self.value.clone(),
            mtext: self.mtext.clone(),
            is_multiline: self.mtext.is_some(),
            locked: self.is_position_locked,
            lock_position_in_block: self.is_position_locked,
            ..Default::default()
        }
    }
    pub fn create_transformed(&self, transformation: crate::matrix3::Matrix3, translation: Vector3) -> Self {
        let mut copy = self.clone();
        copy.position = transformation * copy.position + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * copy.normal) {
            copy.normal = n;
        }
        copy
    }
}
