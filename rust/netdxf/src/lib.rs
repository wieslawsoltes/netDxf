//! High-performance, idiomatic Rust port of the `netDxf` AutoCAD DXF library.
//!
//! Preserves the upstream `netDxf` file layout and public API object model while using
//! idiomatic, 100% safe, `Send + Sync` Rust types.

#![forbid(unsafe_code)]

pub mod aci_color;
pub mod bezier_curve_cubic;
pub mod bezier_curve_quadratic;
pub mod blocks;
pub mod bounding_rectangle;
pub mod clipping_boundary;
pub mod clipping_boundary_type;
pub mod collections;
pub mod coordinate_system;
pub mod dxf_class;
pub mod dxf_dimension_picture_options;
pub mod dxf_document;
pub mod dxf_object;
pub mod dxf_object_code;
pub mod dxf_version_compatibility_report;
pub mod entities;
pub mod error;
pub mod gte;
pub mod header;
pub mod io;
pub mod lineweight;
pub mod math_helper;
pub mod matrix2;
pub mod matrix3;
pub mod matrix4;
pub mod objects;
pub mod subclass_marker;
pub mod symbols;
pub mod tables;
pub mod transparency;
pub mod units;
pub mod vector2;
pub mod vector3;
pub mod vector4;
pub mod x_data;
pub mod x_data_code;
pub mod x_data_record;

pub use aci_color::AciColor;
pub use bezier_curve_cubic::BezierCurveCubic;
pub use bezier_curve_quadratic::BezierCurveQuadratic;
pub use bounding_rectangle::BoundingRectangle;
pub use clipping_boundary::ClippingBoundary;
pub use clipping_boundary_type::ClippingBoundaryType;
pub use coordinate_system::CoordinateSystem;
pub use dxf_class::DxfClass;
pub use dxf_dimension_picture_options::DxfDimensionPictureOptions;
pub use dxf_document::{DxfDocument, DxfSplineCountPolicy};
pub use dxf_object::DxfObject;
pub use dxf_object_code::DxfObjectCode;
pub use dxf_version_compatibility_report::{
    DxfVersionCompatibilityDiagnostic, DxfVersionCompatibilityIssue, DxfVersionCompatibilityKind,
    DxfVersionCompatibilityReport,
};
pub use error::{DxfError, Result};
pub use lineweight::Lineweight;
pub use math_helper::MathHelper;
pub use matrix2::Matrix2;
pub use matrix3::Matrix3;
pub use matrix4::Matrix4;
pub use subclass_marker::SubclassMarker;
pub use symbols::Symbols;
pub use transparency::Transparency;
pub use vector2::Vector2;
pub use vector3::Vector3;
pub use vector4::Vector4;
pub use x_data::XData;
pub use x_data_code::XDataCode;
pub use x_data_record::{XDataRecord, XDataValue};
