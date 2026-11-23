use rustc_hash::FxHashMap;

use crate::dxf_object::DxfObject;
use crate::error::{DxfError, Result};
use crate::tables::DxfTableFormula;

/// Represents a stored DXF `FIELD` object (`netDxf.Objects.DxfStoredField`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfStoredField {
    pub object: DxfObject,
    pub evaluator_id: String,
    pub field_code: String,
    pub format_string: String,
    pub evaluated_text: String,
    pub evaluation_status: DxfFieldResultStatus,
    pub evaluation_error_code: i32,
    pub evaluation_error_message: String,
    pub value: DxfFieldDataValue,
    pub filing_option: i32,
    pub evaluation_option: i32,
    pub object_ids: Vec<String>,
    pub children: Vec<DxfStoredField>,
}

impl DxfStoredField {
    pub const MAXIMUM_PROJECTED_BINARY_BYTES: usize = 4 * 1024 * 1024;
    pub const MAXIMUM_RESULT_PAYLOAD_TAGS: usize = 65_536;

    pub fn new(evaluator_id: impl Into<String>, field_code: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new("FIELD"),
            evaluator_id: evaluator_id.into(),
            field_code: field_code.into(),
            format_string: String::new(),
            evaluated_text: String::new(),
            evaluation_status: DxfFieldResultStatus::Success,
            evaluation_error_code: 0,
            evaluation_error_message: String::new(),
            value: DxfFieldDataValue::Empty,
            filing_option: 0,
            evaluation_option: 0,
            object_ids: Vec::new(),
            children: Vec::new(),
        }
    }

    pub fn apply_result(&mut self, result: &DxfFieldResultEdit) {
        self.evaluation_status = result.status;
        self.evaluated_text = result.formatted_text.clone();
        self.value = result.value.clone();
    }

    #[inline] pub fn evaluation(&self) -> DxfFieldResultStatus { self.evaluation_status }
    #[inline] pub fn referenced_objects(&self) -> &[String] { &self.object_ids }
    #[inline] pub fn references(&self) -> &[String] { &self.object_ids }
    #[inline] pub fn payload(&self) -> &str { &self.evaluated_text }
    #[inline] pub fn source_version(&self) -> Option<crate::header::DxfVersion> { None }
}

/// Result of evaluating a standard DXF field (`netDxf.Objects.DxfFieldResult`).
#[derive(Clone, Debug, PartialEq)]
pub enum DxfFieldResult {
    Text(String),
    Number(f64),
    Failure {
        status: DxfFieldResultStatus,
        error_code: i32,
        error_message: String,
        cached_value: Option<String>,
    },
}

impl DxfFieldResult {
    pub fn to_display_string(&self) -> String {
        match self {
            Self::Text(s) => s.clone(),
            Self::Number(v) => format!("{v}"),
            Self::Failure { cached_value, .. } => cached_value.clone().unwrap_or_default(),
        }
    }

    pub fn status(&self) -> DxfFieldResultStatus {
        match self {
            Self::Text(_) | Self::Number(_) => DxfFieldResultStatus::Success,
            Self::Failure { status, .. } => *status,
        }
    }

    pub fn formatted_text(&self) -> String {
        self.to_display_string()
    }

    pub fn value(&self) -> DxfFieldDataValue {
        match self {
            Self::Text(s) => DxfFieldDataValue::String(s.clone()),
            Self::Number(v) => DxfFieldDataValue::Double(*v),
            Self::Failure { cached_value, .. } => cached_value
                .as_ref()
                .map_or(DxfFieldDataValue::Empty, |s| DxfFieldDataValue::String(s.clone())),
        }
    }

    pub fn value_display_text(&self) -> String {
        self.to_display_string()
    }

    pub fn error_code(&self) -> i32 {
        match self {
            Self::Failure { error_code, .. } => *error_code,
            _ => 0,
        }
    }

    pub fn error_message(&self) -> &str {
        match self {
            Self::Failure { error_message, .. } => error_message.as_str(),
            _ => "",
        }
    }

    pub fn retains_cached_value(&self) -> bool {
        matches!(self, Self::Failure { cached_value: Some(_), .. })
    }

    pub fn failure(
        status: DxfFieldResultStatus,
        error_code: i32,
        error_message: impl Into<String>,
    ) -> Self {
        Self::Failure {
            status,
            error_code,
            error_message: error_message.into(),
            cached_value: None,
        }
    }

    pub fn failure_with_value(
        status: DxfFieldResultStatus,
        error_code: i32,
        error_message: impl Into<String>,
        cached_value: impl Into<String>,
    ) -> Self {
        Self::Failure {
            status,
            error_code,
            error_message: error_message.into(),
            cached_value: Some(cached_value.into()),
        }
    }
}

/// Evaluates standard DXF field expressions (`AcExpr`, `AcVar`, `AcSm`) safely without scripts (`netDxf.Objects.DxfStandardFieldEvaluator`).
#[derive(Clone, Debug, Default)]
pub struct DxfStandardFieldEvaluator {
    pub variables: FxHashMap<String, String>,
}

impl DxfStandardFieldEvaluator {
    pub const MAXIMUM_VARIABLES: usize = 4_096;

    pub fn new() -> Self {
        Self::default()
    }

    pub fn evaluate_or_throw(&self, field_code: &str) -> Result<DxfFieldResult> {
        self.evaluate(field_code)
    }

    pub fn set_variable(&mut self, name: impl Into<String>, value: impl Into<String>) {
        self.variables.insert(name.into().to_ascii_uppercase(), value.into());
    }

    /// Evaluates a DXF field code such as `%<\AcExpr (2+3*4)>%` or `%<\AcVar Filename>%`.
    pub fn evaluate(&self, field_code: &str) -> Result<DxfFieldResult> {
        let trimmed = field_code.trim();
        let inner = trimmed
            .strip_prefix("%<\\")
            .and_then(|s| s.strip_suffix(">%"))
            .ok_or_else(|| {
                DxfError::InvalidValue(format!("Invalid DXF field syntax: '{field_code}'"))
            })?;

        let mut parts = inner.splitn(2, char::is_whitespace);
        let evaluator = parts.next().unwrap_or("");
        let rest = parts.next().unwrap_or("").trim();
        // Strip optional format specifier `\f "..."`
        let body = rest.split("\\f").next().unwrap_or(rest).trim();

        match evaluator {
            "AcExpr" => {
                let expr_str = format!("={body}");
                let formula = DxfTableFormula::parse_scalar(&expr_str)?;
                let value = formula.evaluate_scalar()?;
                Ok(DxfFieldResult::Number(value))
            }
            "AcVar" => {
                let key = body.to_ascii_uppercase();
                let val = self.variables.get(&key).cloned().ok_or_else(|| {
                    DxfError::NotFound {
                        kind: "FieldVariable",
                        name: body.to_string(),
                    }
                })?;
                Ok(DxfFieldResult::Text(val))
            }
            _ => Err(DxfError::InvalidValue(format!(
                "Unsupported DXF field evaluator '{evaluator}'"
            ))),
        }
    }
}

/// Evaluation status code for a DXF `FIELD` object (group 92).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum DxfFieldResultStatus {
    #[default]
    Success = 2,
    EvaluatorNotFound = 4,
    SyntaxError = 8,
    InvalidCode = 16,
    InvalidContext = 32,
    OtherError = 64,
}

/// Typed data value carried by a DXF `FIELD` evaluation result.
#[derive(Debug, Clone, PartialEq)]
pub enum DxfFieldDataValue {
    Empty,
    Integer(i32),
    Double(f64),
    String(String),
    Point3D(crate::vector3::Vector3),
}

impl DxfFieldDataValue {
    #[inline]
    pub fn name(&self) -> &'static str {
        match self {
            Self::Empty => "Empty",
            Self::Integer(_) => "Integer",
            Self::Double(_) => "Double",
            Self::String(_) => "String",
            Self::Point3D(_) => "Point3D",
        }
    }

    #[inline]
    pub fn tags(&self) -> Vec<(i32, String)> {
        Vec::new()
    }

    #[inline]
    pub fn value(&self) -> String {
        match self {
            Self::Empty => String::new(),
            Self::Integer(v) => v.to_string(),
            Self::Double(v) => v.to_string(),
            Self::String(s) => s.clone(),
            Self::Point3D(p) => format!("{},{},{}", p.x, p.y, p.z),
        }
    }
}

/// Input parameters passed to a field evaluator.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfFieldEvaluationInput {
    pub field_code: String,
    pub format_string: String,
    pub evaluator_id: String,
    pub children: Vec<String>,
    pub field: Option<String>,
    pub evaluation: DxfFieldResultStatus,
}

impl DxfFieldEvaluationInput {
    pub fn compose_text(&self) -> String {
        self.field_code.clone()
    }
}

/// Mutable edit payload for updating a stored `FIELD` result.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfFieldResultEdit {
    pub status: DxfFieldResultStatus,
    pub formatted_text: String,
    pub value: DxfFieldDataValue,
}

impl DxfFieldResultEdit {
    #[inline] pub fn original(&self) -> &Self { self }
    #[inline] pub fn result(&self) -> &Self { self }
}
