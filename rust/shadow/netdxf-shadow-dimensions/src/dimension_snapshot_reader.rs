use crate::DimensionProjectionOptions;
use netdxf::entities::{Dimension, DimensionType};
use netdxf::tables::DimensionStyle;
use netdxf::{AciColor, Vector2, Vector3};
use netdxf_shadow::{ShadowReader, ShadowSnapshot, Vec3};

/// Rehydrates a native [`netdxf::entities::Dimension`] and [`netdxf::tables::DimensionStyle`]
/// from a detached [`ShadowSnapshot`] entity node (mirroring `DimensionSnapshotReader.cs`).
pub struct DimensionSnapshotReader;

impl DimensionSnapshotReader {
    /// Returns `true` if `type_name` is a supported DXF dimension entity type.
    pub fn is_dimension_type(type_name: &str) -> bool {
        matches!(
            type_name,
            "LinearDimension"
                | "AlignedDimension"
                | "RadialDimension"
                | "DiametricDimension"
                | "Angular2LineDimension"
                | "Angular3PointDimension"
                | "OrdinateDimension"
                | "ArcLengthDimension"
                | "Dimension"
        )
    }

    /// Reads a dimension entity and its effective style from `snapshot`.
    pub fn read(
        snapshot: &ShadowSnapshot,
        entity: &ShadowReader<'_>,
        options: &DimensionProjectionOptions,
    ) -> Option<(Dimension, DimensionStyle)> {
        let ty = entity.type_name();
        if !Self::is_dimension_type(ty) {
            return None;
        }

        let style = Self::read_style(snapshot, entity);

        let p1 = to_v2(entity.field("FirstReferencePoint").vector(Vec3::ZERO));
        let p2 = to_v2(entity.field("SecondReferencePoint").vector(Vec3::ZERO));
        let def = to_v2(entity.field("DefinitionPoint").vector(Vec3::ZERO));
        let offset = entity.field("Offset").number(1.0);
        let rotation = entity.field("Rotation").number(0.0);

        let mut dim = match ty {
            "LinearDimension" => Dimension::linear(p1, p2, offset, rotation),
            "AlignedDimension" => Dimension::aligned(p1, p2, offset),
            "RadialDimension" => {
                let center = to_v2(entity.field("CenterPoint").vector(Vec3::ZERO));
                let reference = to_v2(entity.field("ReferencePoint").vector(Vec3::new(1.0, 0.0, 0.0)));
                Dimension::radial(center, reference)
            }
            "DiametricDimension" => {
                let center = to_v2(entity.field("CenterPoint").vector(Vec3::ZERO));
                let reference = to_v2(entity.field("ReferencePoint").vector(Vec3::new(1.0, 0.0, 0.0)));
                Dimension::diametric(center, reference)
            }
            "Angular2LineDimension" => {
                let mut d = Dimension::linear(p1, p2, offset, 0.0);
                d.dimension_type = DimensionType::Angular;
                d.first_reference_point = to_v3(entity.field("StartFirstLine").vector(Vec3::ZERO));
                d.second_reference_point = to_v3(entity.field("EndFirstLine").vector(Vec3::new(1.0, 0.0, 0.0)));
                d.arc_definition_point = to_v3(entity.field("StartSecondLine").vector(Vec3::ZERO));
                d.definition_point = to_v3(entity.field("EndSecondLine").vector(Vec3::new(0.0, 1.0, 0.0)));
                d
            }
            "Angular3PointDimension" => {
                let mut d = Dimension::linear(p1, p2, offset, 0.0);
                d.dimension_type = DimensionType::Angular3Point;
                d.definition_point = to_v3(entity.field("CenterPoint").vector(Vec3::ZERO));
                d.first_reference_point = to_v3(entity.field("StartPoint").vector(Vec3::new(1.0, 0.0, 0.0)));
                d.second_reference_point = to_v3(entity.field("EndPoint").vector(Vec3::new(0.0, 1.0, 0.0)));
                d
            }
            "OrdinateDimension" => {
                let mut d = Dimension::linear(p1, p2, offset, 0.0);
                d.dimension_type = DimensionType::Ordinate;
                d.first_reference_point = to_v3(entity.field("FeaturePoint").vector(Vec3::ZERO));
                d.second_reference_point = to_v3(entity.field("LeaderEndPoint").vector(Vec3::new(1.0, 1.0, 0.0)));
                d
            }
            "ArcLengthDimension" => {
                let mut d = Dimension::linear(p1, p2, offset, 0.0);
                d.dimension_type = DimensionType::ArcLength;
                d.first_reference_point = Vector3::new(p1.x, p1.y, 0.0);
                d.second_reference_point = Vector3::new(p2.x, p2.y, 0.0);
                d.definition_point = Vector3::new(def.x, def.y, 0.0);
                d
            }
            _ => {
                let mut d = Dimension::linear(p1, p2, offset, rotation);
                d.definition_point = Vector3::new(def.x, def.y, 0.0);
                d
            }
        };

        if entity.field("Elevation").exists() {
            dim.elevation = entity.field("Elevation").number(0.0);
        }
        if entity.field("Normal").exists() {
            let n = entity.field("Normal").vector(Vec3::UNIT_Z);
            if n.is_finite() && n.length() > 0.0 {
                dim.common.normal = to_v3(n.normalized());
            }
        }
        if entity.field("TextRotation").exists() {
            dim.text_rotation = entity.field("TextRotation").number(0.0);
        }
        if entity.field("UserText").exists() {
            let user_text = entity.field("UserText").text().to_string();
            if user_text.len() <= options.maximum_text_length {
                dim.user_text = Some(user_text);
            }
        }
        if entity.field("TextReferencePoint").exists() {
            dim.text_reference_point = to_v2(entity.field("TextReferencePoint").vector(Vec3::ZERO));
        }

        dim.update();
        Some((dim, style))
    }

    fn read_style(_snapshot: &ShadowSnapshot, entity: &ShadowReader<'_>) -> DimensionStyle {
        let mut style = DimensionStyle::default();
        let s = entity.field("Style");
        if s.exists() {
            if s.field("DimScaleOverall").exists() {
                let v = s.field("DimScaleOverall").number(1.0);
                if v > 0.0 && v.is_finite() {
                    style.dim_scale_overall = v;
                }
            }
            if s.field("ArrowSize").exists() {
                let v = s.field("ArrowSize").number(style.arrow_size);
                if v >= 0.0 && v.is_finite() {
                    style.arrow_size = v;
                }
            }
            if s.field("TextHeight").exists() {
                let v = s.field("TextHeight").number(style.text_height);
                if v > 0.0 && v.is_finite() {
                    style.text_height = v;
                }
            }
            if s.field("ExtLineOffset").exists() {
                style.ext_line_offset = s.field("ExtLineOffset").number(style.ext_line_offset);
            }
            if s.field("ExtLineExtend").exists() {
                style.ext_line_extend = s.field("ExtLineExtend").number(style.ext_line_extend);
            }
            if s.field("ExtLine1Off").exists() {
                style.ext_line1_off = s.field("ExtLine1Off").boolean(false);
            }
            if s.field("ExtLine2Off").exists() {
                style.ext_line2_off = s.field("ExtLine2Off").boolean(false);
            }
            if s.field("DimLine1Off").exists() {
                style.dim_line1_off = s.field("DimLine1Off").boolean(false);
            }
            if s.field("DimLine2Off").exists() {
                style.dim_line2_off = s.field("DimLine2Off").boolean(false);
            }
            if s.field("DimLineColor").exists() {
                let idx = s.field("DimLineColor").field("Index").number(0.0) as i16;
                if let Ok(c) = AciColor::from_index(idx) {
                    style.dim_line_color = c;
                }
            }
            if s.field("TextColor").exists() {
                let idx = s.field("TextColor").field("Index").number(0.0) as i16;
                if let Ok(c) = AciColor::from_index(idx) {
                    style.text_color = c;
                }
            }
        }
        style
    }
}

fn to_v2(v: Vec3) -> Vector2 {
    Vector2::new(v.x, v.y)
}

fn to_v3(v: Vec3) -> Vector3 {
    Vector3::new(v.x, v.y, v.z)
}
