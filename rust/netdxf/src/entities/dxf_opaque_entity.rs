use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;

/// Raw DXF `(group_code, value)` tag for opaque/round-tripped entities.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfTag {
    pub code: i16,
    pub value: String,
}

impl DxfTag {
    pub fn new(code: i16, value: impl Into<String>) -> Self {
        Self {
            code,
            value: value.into(),
        }
    }
}

/// Preserves unsupported or custom DXF entities losslessly across read/write round trips.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfOpaqueEntity {
    pub common: EntityCommon,
    pub entity_name: String,
    pub raw_tags: Vec<DxfTag>,
    pub source_version: Option<crate::header::DxfVersion>,
}

impl DxfOpaqueEntity {
    pub fn new(entity_name: impl Into<String>, raw_tags: Vec<DxfTag>) -> Self {
        let name = entity_name.into();
        let mut common = EntityCommon::new(EntityType::Opaque, "");
        common.object.code_name = name.clone();
        Self {
            common,
            entity_name: name,
            raw_tags,
            source_version: None,
        }
    }

    pub fn normal(&self) -> crate::vector3::Vector3 {
        self.common.normal
    }

    pub fn source_handle(&self) -> Option<&str> {
        self.common.object.handle.as_deref()
    }

    pub fn source_tags(&self) -> &[DxfTag] {
        &self.raw_tags
    }

    pub fn references(&self) -> Vec<&str> {
        self.raw_tags
            .iter()
            .filter(|t| matches!(t.code, 330..=369 | 390..=399 | 480..=481))
            .map(|t| t.value.as_str())
            .collect()
    }

    pub fn transform_by(
        &mut self,
        transformation: crate::matrix3::Matrix3,
        _translation: crate::vector3::Vector3,
    ) {
        let new_normal = crate::vector3::Vector3::normalize(transformation * self.common.normal);
        if !crate::vector3::Vector3::is_nan(new_normal) {
            self.common.normal = new_normal;
        }
    }
}
