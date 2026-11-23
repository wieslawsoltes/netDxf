//! Studio HTTP/RPC host adapters (`netDxf.Shadow.Studio.Hosting`).

#![forbid(unsafe_code)]

pub mod studio_agent_http;
pub mod studio_cad_http;
pub mod studio_document_hub;
pub mod studio_http;
pub mod studio_legacy_http;

pub use studio_agent_http::*;
pub use studio_cad_http::*;
pub use studio_document_hub::*;
pub use studio_http::*;
pub use studio_legacy_http::*;
