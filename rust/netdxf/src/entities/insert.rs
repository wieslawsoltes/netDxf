use crate::blocks::Block;
use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::attribute::Attribute;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::units::DrawingUnits;
use crate::vector3::Vector3;

/// Represents a DXF `INSERT` (block reference) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Insert {
    pub common: EntityCommon,
    pub block_name: String,
    pub position: Vector3,
    pub scale: Vector3,
    /// Rotation angle in degrees.
    pub rotation: f64,
    pub attributes: Vec<Attribute>,
    pub column_count: i16,
    pub row_count: i16,
    pub column_spacing: f64,
    pub row_spacing: f64,
}

impl Insert {
    pub const DEFAULT_INS_UNITS: DrawingUnits = DrawingUnits::Unitless;

    pub fn new(block_name: impl Into<String>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Insert, DxfObjectCode::INSERT),
            block_name: block_name.into(),
            position: Vector3::ZERO,
            scale: Vector3::new(1.0, 1.0, 1.0),
            rotation: 0.0,
            attributes: Vec::new(),
            column_count: 1,
            row_count: 1,
            column_spacing: 0.0,
            row_spacing: 0.0,
        }
    }

    pub fn with_position(block_name: impl Into<String>, position: Vector3) -> Self {
        let mut ins = Self::new(block_name);
        ins.position = position;
        ins
    }

    /// Creates an `Insert` for `block` and populates `attributes` from the block's `attribute_definitions`.
    pub fn create_with_attributes(block: &Block, position: Vector3) -> Self {
        let mut ins = Self::with_position(block.table.name.clone(), position);
        ins.sync_attributes(block);
        ins
    }

    #[inline]
    pub fn block(&self) -> &str {
        &self.block_name
    }

    #[inline]
    pub fn end_sequence_record(&self) -> Option<&str> {
        None
    }

    #[inline]
    pub fn ensure_attribute_sequence_end(&mut self) {}

    pub fn get_transformation(&self) -> Matrix3 {
        let ocs_to_wcs = MathHelper::arbitrary_axis(self.common.normal);
        let rot = Matrix3::rotation_z(self.rotation * MathHelper::DEG_TO_RAD);
        let scale = Matrix3::scale(self.scale.x, self.scale.y, self.scale.z);
        ocs_to_wcs * rot * scale
    }

    pub fn get_grid_position(&self, row: usize, column: usize) -> Vector3 {
        let ocs_to_wcs = MathHelper::arbitrary_axis(self.common.normal);
        let rot = Matrix3::rotation_z(self.rotation * MathHelper::DEG_TO_RAD);
        let grid_local = Vector3::new(
            column as f64 * self.column_spacing,
            row as f64 * self.row_spacing,
            0.0,
        );
        self.position + ocs_to_wcs * (rot * grid_local)
    }

    pub fn explode_cell(&self, block: &Block, row: usize, column: usize) -> Vec<EntityObject> {
        let lin = self.get_transformation();
        let translation = self.get_grid_position(row, column) - (lin * block.origin);
        block
            .entities
            .iter()
            .map(|ent| {
                let mut cloned = ent.clone();
                cloned.transform_by(lin, translation);
                cloned
            })
            .collect()
    }

    pub fn explode_enumerable(&self, block: &Block) -> Vec<EntityObject> {
        self.explode(block)
    }

    /// Synchronizes `attributes` against `block.attribute_definitions`, preserving existing values by tag.
    pub fn sync_attributes(&mut self, block: &Block) {
        let existing = std::mem::take(&mut self.attributes);
        for attdef in block.attribute_definitions.values() {
            if let Some(prev) = existing
                .iter()
                .find(|a| a.tag.eq_ignore_ascii_case(&attdef.tag))
            {
                self.attributes.push(prev.clone());
            } else {
                let mut attr = Attribute::new(attdef.tag.clone(), attdef.value.clone());
                attr.position = attdef.position + self.position;
                attr.height = attdef.height;
                attr.rotation = attdef.rotation + self.rotation;
                attr.style = attdef.style.clone();
                attr.layer = attdef.layer.clone();
                attr.color = attdef.color;
                self.attributes.push(attr);
            }
        }
    }

    /// Explodes this `Insert` against its `Block` definition into world-space entities
    /// (including `row_count x column_count` MINSERT grid instances).
    pub fn explode(&self, block: &Block) -> Vec<EntityObject> {
        let ocs_to_wcs = MathHelper::arbitrary_axis(self.common.normal);
        let rot = Matrix3::rotation_z(self.rotation * MathHelper::DEG_TO_RAD);
        let scale = Matrix3::scale(self.scale.x, self.scale.y, self.scale.z);
        let lin = ocs_to_wcs * rot * scale;

        let rows = self.row_count.max(1) as usize;
        let cols = self.column_count.max(1) as usize;
        let mut out = Vec::with_capacity(block.entities.len() * rows * cols);

        for r in 0..rows {
            for c in 0..cols {
                let grid_local = Vector3::new(
                    c as f64 * self.column_spacing,
                    r as f64 * self.row_spacing,
                    0.0,
                );
                let grid_offset = ocs_to_wcs * (rot * grid_local);
                let translation = self.position + grid_offset - (lin * block.origin);
                for ent in &block.entities {
                    let mut cloned = ent.clone();
                    cloned.transform_by(lin, translation);
                    out.push(cloned);
                }
            }
        }
        out
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.position = transformation * self.position + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.common.normal) {
            self.common.normal = n;
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::new(self.position.to_vector2(), self.position.to_vector2())
    }
}
