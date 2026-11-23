//! FerroUI WebAssembly studio runtime bridge (`netDxf.Shadow.Studio.WebAssembly`).

#![forbid(unsafe_code)]

pub mod browser_mcp;
pub mod browser_runtime;

pub use browser_mcp::*;
pub use browser_runtime::*;
