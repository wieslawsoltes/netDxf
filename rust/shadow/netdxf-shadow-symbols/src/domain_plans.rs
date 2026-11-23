#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct DomainPlans;

impl DomainPlans {
    pub fn schedule(scan: &DomainScan, options: &DomainScheduleOptions) -> DomainSchedule {
        let mut by_group: BTreeMap<String, Vec<DomainScheduleRow>> = BTreeMap::new();
        for e in &scan.entities {
            let group_key = options
                .group_by
                .as_deref()
                .and_then(|k| e.record.properties.get(k).cloned())
                .unwrap_or_else(|| e.record.domain.clone());
            by_group.entry(group_key).or_default().push(DomainScheduleRow {
                entity: e.entity.clone(),
                tag: e.record.tag.clone(),
                columns: e.record.properties.clone(),
                ..Default::default()
            });
        }
        let groups = by_group
            .into_iter()
            .map(|(key, rows)| DomainScheduleGroup { key, rows })
            .collect();
        DomainSchedule {
            groups,
            issues: Vec::new(),
            complete: true,
            ..Default::default()
        }
    }
}

