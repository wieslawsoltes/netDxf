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
    pub evaluated_text: String,
    pub children: Vec<DxfStoredField>,
}

impl DxfStoredField {
    pub fn new(evaluator_id: impl Into<String>, field_code: impl Into<String>) -> Self {
        Self {
            object: DxfObject::new("FIELD"),
            evaluator_id: evaluator_id.into(),
            field_code: field_code.into(),
            evaluated_text: String::new(),
            children: Vec::new(),
        }
    }
}

/// Result of evaluating a standard DXF field (`netDxf.Objects.DxfFieldResult`).
#[derive(Clone, Debug, PartialEq)]
pub enum DxfFieldResult {
    Text(String),
    Number(f64),
}

impl DxfFieldResult {
    pub fn to_display_string(&self) -> String {
        match self {
            Self::Text(s) => s.clone(),
            Self::Number(v) => format!("{v}"),
        }
    }
}

/// Evaluates standard DXF field expressions (`AcExpr`, `AcVar`, `AcSm`) safely without scripts (`netDxf.Objects.DxfStandardFieldEvaluator`).
#[derive(Clone, Debug, Default)]
pub struct DxfStandardFieldEvaluator {
    pub variables: FxHashMap<String, String>,
}

impl DxfStandardFieldEvaluator {
    pub fn new() -> Self {
        Self::default()
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
