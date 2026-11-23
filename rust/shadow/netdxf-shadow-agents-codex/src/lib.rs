//! Codex app-server client (`netDxf.Shadow.Agents.Codex`).

#![forbid(unsafe_code)]

pub mod codex_app_server_client;
pub mod codex_contracts;

pub use codex_app_server_client::CodexAppServerClient;
pub use codex_contracts::{
    CodexNotification, CodexProcessOptions, CodexRpcException, CodexServerRequest,
};
