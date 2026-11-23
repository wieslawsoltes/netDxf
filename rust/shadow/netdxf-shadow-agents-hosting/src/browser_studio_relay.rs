#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
use std::collections::BTreeMap;

#[derive(Debug, Clone, Default)]
pub struct BrowserStudioRelay {
    pub connected: bool,
    pub capacity: usize,
    pub names: Vec<String>,
    pub pending: BTreeMap<String, Value>,
}

impl BrowserStudioRelay {
    pub fn new(capacity: usize) -> Self {
        Self {
            connected: false,
            capacity: capacity.max(1),
            names: Vec::new(),
            pending: BTreeMap::new(),
        }
    }

    pub fn route_prefix() -> &'static str {
        "/api/studio/relay"
    }

    #[inline]
    pub fn is_connected(&self) -> bool {
        self.connected
    }

    #[inline]
    pub fn pending_count(&self) -> usize {
        self.pending.len()
    }

    pub fn connect(&mut self) {
        self.connected = true;
    }

    pub fn stop(&mut self) {
        self.connected = false;
        self.pending.clear();
    }

    pub fn enqueue(&mut self, request_id: impl Into<String>, payload: Value) -> Result<(), String> {
        if self.pending.len() >= self.capacity {
            return Err("Relay capacity exceeded".into());
        }
        self.pending.insert(request_id.into(), payload);
        Ok(())
    }

    pub fn cancel(&mut self, request_id: &str) -> bool {
        self.pending.remove(request_id).is_some()
    }
}
