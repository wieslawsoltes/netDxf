//! Built-in CAD sample drawings (`netDxf.Shadow.Samples`).

#![forbid(unsafe_code)]

use netdxf::entities::{Circle, EntityObject, Line};
use netdxf::{DxfDocument, Vector3};

pub struct ProcessDiagramFactory;

impl ProcessDiagramFactory {
    pub fn create_basic_diagram() -> DxfDocument {
        let mut doc = DxfDocument::new();
        doc.entities.add(EntityObject::Line(Box::new(Line::new(
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(100.0, 0.0, 0.0),
        ))));
        doc.entities.add(EntityObject::Circle(Box::new(Circle::new(
            Vector3::new(50.0, 0.0, 0.0),
            10.0,
        ))));
        doc
    }
}

pub struct StudioSamples;

impl StudioSamples {
    pub fn names() -> &'static [&'static str] {
        &["process-diagram", "architectural-plan", "mechanical-part"]
    }

    pub fn create(name: &str) -> DxfDocument {
        let _ = name;
        ProcessDiagramFactory::create_basic_diagram()
    }
}
