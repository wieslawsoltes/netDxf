//! Extensible CAD discipline module contracts and registry (`netDxf.Shadow.Modules`).

#![forbid(unsafe_code)]

pub mod cad_module_contracts;
pub mod cad_module_registry;

pub use cad_module_contracts::*;
pub use cad_module_registry::*;
