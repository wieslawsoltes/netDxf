//! External agent host and studio relay (`netDxf.Shadow.Agents.Hosting`).

#![forbid(unsafe_code)]

pub mod agent_host_application;
pub mod agent_host_options;
pub mod browser_studio_relay;
pub mod chat_gpt_host_session;
pub mod companion_workspace;
pub mod external_agent_host;
pub mod external_agent_http;

pub use agent_host_application::*;
pub use agent_host_options::*;
pub use browser_studio_relay::*;
pub use chat_gpt_host_session::*;
pub use companion_workspace::*;
pub use external_agent_host::*;
pub use external_agent_http::*;
