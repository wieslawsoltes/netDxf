use crate::blocks::Block;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::objects::plot_settings::PlotSettings;
use crate::tables::{TableObject, TableObjectData};
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `LAYOUT` (Model or Paper Space tab).
#[derive(Clone, Debug, PartialEq)]
pub struct Layout {
    pub table: TableObjectData,
    pub plot_settings: PlotSettings,
    pub min_limit: Vector2,
    pub max_limit: Vector2,
    pub min_extents: Vector3,
    pub max_extents: Vector3,
    pub base_point: Vector3,
    pub elevation: f64,
    pub origin: Vector3,
    pub x_axis: Vector3,
    pub y_axis: Vector3,
    pub tab_order: i16,
    pub associated_block_name: String,
    pub is_paper_space: bool,
}

impl Layout {
    pub const MODEL_SPACE_NAME: &'static str = "Model";

    pub fn model_space() -> Self {
        Self {
            table: TableObjectData::reserved(Self::MODEL_SPACE_NAME, DxfObjectCode::LAYOUT),
            plot_settings: PlotSettings::default(),
            min_limit: Vector2::new(-20.0, -7.5),
            max_limit: Vector2::new(277.0, 202.5),
            min_extents: Vector3::ZERO,
            max_extents: Vector3::ZERO,
            base_point: Vector3::ZERO,
            elevation: 0.0,
            origin: Vector3::ZERO,
            x_axis: Vector3::UNIT_X,
            y_axis: Vector3::UNIT_Y,
            tab_order: 0,
            associated_block_name: Block::MODEL_SPACE_NAME.to_string(),
            is_paper_space: false,
        }
    }

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::LAYOUT, true)?,
            plot_settings: PlotSettings::default(),
            min_limit: Vector2::new(-20.0, -7.5),
            max_limit: Vector2::new(277.0, 202.5),
            min_extents: Vector3::ZERO,
            max_extents: Vector3::ZERO,
            base_point: Vector3::ZERO,
            elevation: 0.0,
            origin: Vector3::ZERO,
            x_axis: Vector3::UNIT_X,
            y_axis: Vector3::UNIT_Y,
            tab_order: 1,
            associated_block_name: Block::PAPER_SPACE_NAME.to_string(),
            is_paper_space: true,
        })
    }

    #[inline] pub fn associated_block(&self) -> &str { &self.associated_block_name }
    #[inline] pub fn ucs_origin(&self) -> Vector3 { self.origin }
    #[inline] pub fn ucs_x_axis(&self) -> Vector3 { self.x_axis }
    #[inline] pub fn ucs_y_axis(&self) -> Vector3 { self.y_axis }
}

impl TableObject for Layout {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
