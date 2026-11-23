use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::mtext_attachment_point::{
    MTextAttachmentPoint, MTextDrawingDirection, MTextLineSpacingStyle,
};
use crate::entities::mtext_background_fill::MTextBackgroundFill;
use crate::entities::mtext_columns::{MTextColumnStorage, MTextColumns};
use crate::matrix3::Matrix3;
use crate::tables::TextStyle;
use crate::vector3::Vector3;

/// Represents a DXF `MTEXT` (multiline text) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct MText {
    pub common: EntityCommon,
    pub value: String,
    pub position: Vector3,
    pub height: f64,
    pub rectangle_width: f64,
    pub defined_height: f64,
    pub attachment_point: MTextAttachmentPoint,
    pub drawing_direction: MTextDrawingDirection,
    pub line_spacing_factor: f64,
    pub line_spacing_style: MTextLineSpacingStyle,
    pub rotation: f64,
    pub style: String,
    pub background_fill: Option<MTextBackgroundFill>,
    pub columns: Option<MTextColumns>,
}

impl MText {
    pub const DEFAULT_MIRR_TEXT: bool = false;

    pub fn new(value: impl Into<String>, position: Vector3, height: f64) -> Self {
        Self {
            common: EntityCommon::new(EntityType::MText, DxfObjectCode::MTEXT),
            value: value.into(),
            position,
            height: height.abs().max(f64::EPSILON),
            rectangle_width: 0.0,
            defined_height: 0.0,
            attachment_point: MTextAttachmentPoint::TopLeft,
            drawing_direction: MTextDrawingDirection::ByStyle,
            line_spacing_factor: 1.0,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
            rotation: 0.0,
            style: TextStyle::DEFAULT_NAME.to_string(),
            background_fill: None,
            columns: None,
        }
    }

    /// Appends a paragraph opening block `{` (optionally with paragraph formatting code).
    pub fn start_paragraph(&mut self, options: Option<&str>) {
        self.value.push('{');
        if let Some(opts) = options {
            if !opts.is_empty() {
                self.value.push_str(opts);
            }
        }
    }

    /// Closes a paragraph block `}` and appends a `\P` line break.
    pub fn end_paragraph(&mut self) {
        self.value.push_str("}\\P");
    }

    /// Appends formatted text to this `MText` value.
    pub fn write(&mut self, text: &str) {
        self.value.push_str(text);
    }

    /// Appends a stacked fraction `{\Snum/den;}` to this `MText` value.
    pub fn write_fraction(&mut self, numerator: &str, denominator: &str) {
        self.value
            .push_str(&format!("{{\\S{numerator}/{denominator};}}"));
    }

    /// Converts the MText column metadata to `MTextColumnStorage::Embedded`.
    pub fn convert_to_embedded_columns(&mut self) {
        let cols = self.columns.get_or_insert_with(MTextColumns::default);
        cols.storage = MTextColumnStorage::Embedded;
        if cols.embedded_insertion_point.is_none() {
            cols.embedded_insertion_point = Some(self.position);
        }
        if cols.embedded_reference_width.is_none() && self.rectangle_width > 0.0 {
            cols.embedded_reference_width = Some(self.rectangle_width);
        }
    }

    /// Converts the MText column metadata to `MTextColumnStorage::LegacyLinked`.
    pub fn convert_to_linked_columns(&mut self) {
        let cols = self.columns.get_or_insert_with(MTextColumns::default);
        cols.storage = MTextColumnStorage::LegacyLinked;
        cols.reset_embedded_placement();
    }

    pub fn plain_text(&self) -> String {
        let mut out = String::with_capacity(self.value.len());
        let mut chars = self.value.chars().peekable();
        while let Some(c) = chars.next() {
            match c {
                '{' | '}' => {}
                '\\' => {
                    if let Some(&next) = chars.peek() {
                        match next {
                            'P' | 'p' => {
                                chars.next();
                                out.push('\n');
                            }
                            '~' => {
                                chars.next();
                                out.push(' ');
                            }
                            '\\' | '{' | '}' => {
                                chars.next();
                                out.push(next);
                            }
                            'L' | 'l' | 'O' | 'o' | 'K' | 'k' => {
                                chars.next();
                            }
                            _ => {
                                for ch in chars.by_ref() {
                                    if ch == ';' {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                _ => out.push(c),
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
