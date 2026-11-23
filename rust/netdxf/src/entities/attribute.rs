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
    pub linetype_scale_value: f64,
    pub definition_tag: Option<String>,
    pub shadow_mode_value: crate::entities::EntityShadowMode,
    pub proxy_graphics_data: Option<Vec<u8>>,
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
            linetype_scale_value: 1.0,
            definition_tag: None,
            shadow_mode_value: crate::entities::EntityShadowMode::CastAndReceive,
            proxy_graphics_data: None,
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
            linetype_scale_value: def.linetype_scale_value,
            definition_tag: Some(def.tag.clone()),
            shadow_mode_value: def.shadow_mode_value,
            proxy_graphics_data: def.proxy_graphics_data.clone(),
            mtext: def.mtext.clone(),
        }
    }

    pub const MAXIMUM_PROXY_GRAPHICS_BYTES: usize = 16 * 1024 * 1024;

    #[inline] pub fn is_visible(&self) -> bool { !self.flags.contains(AttributeFlags::HIDDEN) }
    #[inline] pub fn has_mtext(&self) -> bool { self.mtext.is_some() }
    #[inline] pub fn get_mtext(&self) -> Option<&crate::entities::AttributeMText> { self.mtext.as_ref() }
    #[inline] pub fn set_mtext(&mut self, mtext: Option<crate::entities::AttributeMText>) { self.mtext = mtext; }
    #[inline] pub fn width(&self) -> f64 { self.width_factor }
    #[inline] pub fn linetype_scale(&self) -> f64 { self.linetype_scale_value }
    #[inline] pub fn owner(&self) -> Option<&str> { self.object.owner_handle.as_deref() }
    #[inline] pub fn definition(&self) -> Option<&str> { self.definition_tag.as_deref() }
    pub fn set_definition(&mut self, def: &AttributeDefinition) {
        self.definition_tag = Some(def.tag.clone());
        self.tag = def.tag.clone();
        self.style = def.style.clone();
        self.height = def.height;
        self.width_factor = def.width_factor;
        self.oblique_angle = def.oblique_angle;
        self.alignment = def.alignment;
        self.flags = def.flags;
    }
    pub fn prepare_definition_binding(
        &self,
        new_tag: Option<String>,
        preserve_value: bool,
    ) -> crate::entities::AttributeDefinitionBindingChange {
        crate::entities::AttributeDefinitionBindingChange {
            previous_tag: self.definition_tag.clone(),
            new_tag,
            preserve_value,
        }
    }
    #[inline] pub fn shadow_mode(&self) -> crate::entities::EntityShadowMode { self.shadow_mode_value }
    #[inline] pub fn proxy_graphics(&self) -> Option<&[u8]> { self.proxy_graphics_data.as_deref() }
    pub fn set_proxy_graphics(&mut self, data: Vec<u8>) {
        if data.len() <= Self::MAXIMUM_PROXY_GRAPHICS_BYTES {
            self.proxy_graphics_data = Some(data);
        }
    }
    #[inline] pub fn clear_proxy_graphics(&mut self) { self.proxy_graphics_data = None; }

    pub fn set_text_data(&mut self, data: &crate::entities::AttributeTextData) {
        self.value = data.text.clone();
        self.mtext = data.mtext.clone();
        self.is_position_locked = data.locked;
    }

    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        self.position = transformation * self.position + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.normal) {
            self.normal = n;
        }
    }
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
