//! Bridge between live `netdxf::DxfDocument` and `netdxf-shadow` snapshots (`netDxf.Shadow.NetDxf`).

#![forbid(unsafe_code)]

pub mod attribute_synchronization;
pub mod insert_attribute_construction;
pub mod shadow_capture;
pub mod shadow_object_factories;
pub mod shadow_session;

pub use attribute_synchronization::{
    AttributeSynchronization, AttributeSynchronizationOptions, AttributeSynchronizationPlan,
    AttributeSynchronizationReference, AttributeSynchronizationRemoval,
};
pub use insert_attribute_construction::{
    InsertAttributeAssignment, InsertAttributeConstruction,
};
pub use shadow_capture::ShadowCaptureOptions;
pub use shadow_object_factories::{IShadowObjectResolver, ShadowObjectFactories};
pub use shadow_session::{ShadowSession, ShadowSourceValidationMode};
