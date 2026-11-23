#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct SymbolNative;

impl SymbolNative {
    pub fn block_name(symbol: &SymbolDefinition) -> String {
        format!("SYM_{}", symbol.id.replace('.', "_").to_ascii_uppercase())
    }

    pub fn create_block(symbol: &SymbolDefinition) -> String {
        Self::block_name(symbol)
    }

    pub fn place(symbol: &SymbolDefinition, position: Vec3) -> (String, Vec3) {
        (Self::block_name(symbol), position)
    }
}

