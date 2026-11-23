//! DXF/DWG drawing exchange (`netDxf.Shadow.Exchange.Dwg`).

#![forbid(unsafe_code)]

pub mod drawing_exchange;

pub use drawing_exchange::{
    DrawingEncoding, DrawingExchange, DrawingExchangeDiagnostic, DrawingExchangeException,
    DrawingExchangeLimits, DrawingExchangeResult,
};
