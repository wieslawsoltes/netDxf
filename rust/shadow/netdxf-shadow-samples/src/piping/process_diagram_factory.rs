#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf::{DxfDocument, Vector3};
#[allow(unused_imports)]
use netdxf::entities::{Circle, EntityObject, Line};

pub struct ProcessDiagramFactory;

impl ProcessDiagramFactory {
    pub const DRAWING_NAME: &'static str = "ProcessDiagram";

    pub fn create() -> DxfDocument {
        Self::create_basic_diagram()
    }

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

