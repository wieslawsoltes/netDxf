#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf::{DxfDocument, Vector3};
#[allow(unused_imports)]
use netdxf::entities::{Circle, EntityObject, Line};

pub struct StudioSamples;

impl StudioSamples {
    pub fn names() -> &'static [&'static str] {
        &["process-diagram", "architectural-plan", "mechanical-part"]
    }

    pub fn create(name: &str) -> DxfDocument {
        let _ = name;
        ProcessDiagramFactory::create_basic_diagram()
    }

    pub fn create_document() -> DxfDocument {
        ProcessDiagramFactory::create_basic_diagram()
    }

    pub fn checker_reference() -> &'static str {
        "checker.png"
    }

    pub fn demo_font() -> &'static str {
        "shadow-demo.shx"
    }
}

