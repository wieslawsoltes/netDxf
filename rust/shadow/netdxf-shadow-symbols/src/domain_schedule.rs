#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DomainScheduleOptions {
    pub group_by: Option<String>,
    pub maximum_records: usize,
    pub maximum_data_characters: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainScheduleRow {
    pub key: String,
    pub entity: ObjectId,
    pub tag: String,
    pub class: String,
    pub r#type: String,
    pub symbol_id: Option<String>,
    pub symbol_version: Option<String>,
    pub segment_length: Option<f64>,
    pub ports: Vec<String>,
    pub references: Vec<String>,
    pub properties: BTreeMap<String, String>,
    pub columns: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainScheduleGroup {
    pub key: String,
    pub rows: Vec<DomainScheduleRow>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DomainScheduleIssue {
    pub code: String,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DomainSchedule {
    pub document_id: String,
    pub layout: String,
    pub drawing_units: String,
    pub field: Option<String>,
    pub complete: bool,
    pub invalid_records: usize,
    pub maximum_records: usize,
    pub maximum_data_characters: usize,
    pub limitations: Vec<String>,
    pub groups: Vec<DomainScheduleGroup>,
    pub issues: Vec<DomainScheduleIssue>,
}

impl DomainSchedule {
    pub fn build(
        document_id: impl Into<String>,
        layout: impl Into<String>,
        rows: Vec<DomainScheduleRow>,
        options: &DomainScheduleOptions,
    ) -> Self {
        let mut grouped: BTreeMap<String, Vec<DomainScheduleRow>> = BTreeMap::new();
        for row in rows {
            let group_key = options
                .group_by
                .as_deref()
                .and_then(|k| row.columns.get(k).or_else(|| row.properties.get(k)))
                .cloned()
                .unwrap_or_else(|| {
                    if row.class.is_empty() {
                        "default".to_string()
                    } else {
                        row.class.clone()
                    }
                });
            grouped.entry(group_key).or_default().push(row);
        }
        let groups = grouped
            .into_iter()
            .map(|(key, rows)| DomainScheduleGroup { key, rows })
            .collect();
        Self {
            document_id: document_id.into(),
            layout: layout.into(),
            drawing_units: "Unitless".into(),
            field: options.group_by.clone(),
            complete: true,
            invalid_records: 0,
            maximum_records: options.maximum_records,
            maximum_data_characters: options.maximum_data_characters,
            limitations: Vec::new(),
            groups,
            issues: Vec::new(),
        }
    }
}
