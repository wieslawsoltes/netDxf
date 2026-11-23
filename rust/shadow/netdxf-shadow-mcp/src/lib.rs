//! Model Context Protocol (MCP) server and CAD tool registry (`netDxf.Shadow.Mcp`).

#![forbid(unsafe_code)]

pub mod drawing_references;
pub mod mcp_json_rpc_session;
pub mod mcp_tool_registry;
pub mod shadow_json;
pub mod shadow_mcp_context;
pub mod shadow_mcp_server;
pub mod shadow_mcp_tools;

pub use drawing_references::*;
pub use mcp_json_rpc_session::*;
pub use mcp_tool_registry::*;
pub use shadow_json::*;
pub use shadow_mcp_context::*;
pub use shadow_mcp_server::*;
pub use shadow_mcp_tools::*;
