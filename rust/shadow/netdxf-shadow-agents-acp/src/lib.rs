//! Agent Client Protocol (ACP) client (`netDxf.Shadow.Agents.Acp`).

#![forbid(unsafe_code)]

pub mod acp_client;
pub mod acp_contracts;

pub use acp_client::AcpClient;
pub use acp_contracts::{
    AcpClientServices, AcpConnectionOptions, AcpException, AcpPermissionRequest, AcpProcessOptions,
};
