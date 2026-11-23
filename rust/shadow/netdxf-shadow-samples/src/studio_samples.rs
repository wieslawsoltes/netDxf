use crate::ProcessDiagramFactory;
use netdxf::entities::{
    Arc, Circle, Dimension, Line, MText, Polyline2D, Polyline2DVertex, Text,
};
use netdxf::tables::Layer;
use netdxf::{AciColor, DxfDocument, Vector2, Vector3};

/// Built-in sample DXF drawings for Shadow Studio (`StudioSamples.cs`).
pub struct StudioSamples;

impl StudioSamples {
    pub fn names() -> &'static [&'static str] {
        &[
            "process-diagram",
            "architectural-plan",
            "mechanical-part",
            "electrical-schematic",
            "typography-and-linetypes",
        ]
    }

    pub fn create(name: &str) -> DxfDocument {
        match name.trim().to_ascii_lowercase().as_str() {
            "architectural-plan" => Self::create_architectural_plan(),
            "mechanical-part" => Self::create_mechanical_part(),
            "electrical-schematic" => Self::create_electrical_schematic(),
            "typography-and-linetypes" => Self::create_typography_sample(),
            _ => ProcessDiagramFactory::create_basic_diagram(),
        }
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

    fn create_architectural_plan() -> DxfDocument {
        let mut doc = DxfDocument::new();
        if let Ok(mut walls) = Layer::new("A-WALL") {
            walls.color = AciColor::DEFAULT;
            let _ = doc.layers.add(walls);
        }
        if let Ok(mut doors) = Layer::new("A-DOOR") {
            doors.color = AciColor::YELLOW;
            let _ = doc.layers.add(doors);
        }
        if let Ok(mut dims) = Layer::new("A-DIMS") {
            dims.color = AciColor::CYAN;
            let _ = doc.layers.add(dims);
        }

        // Exterior shell 12000 x 8000 mm
        let mut shell = Polyline2D::new(
            vec![
                Polyline2DVertex::new(0.0, 0.0),
                Polyline2DVertex::new(12000.0, 0.0),
                Polyline2DVertex::new(12000.0, 8000.0),
                Polyline2DVertex::new(0.0, 8000.0),
            ],
            true,
        );
        shell.common.layer = "A-WALL".into();
        doc.add_entity(shell);

        // Interior partition wall with doorway gap
        let mut part1 = Line::new(
            Vector3::new(6000.0, 0.0, 0.0),
            Vector3::new(6000.0, 3500.0, 0.0),
        );
        part1.common.layer = "A-WALL".into();
        doc.add_entity(part1);

        let mut part2 = Line::new(
            Vector3::new(6000.0, 4500.0, 0.0),
            Vector3::new(6000.0, 8000.0, 0.0),
        );
        part2.common.layer = "A-WALL".into();
        doc.add_entity(part2);

        // Door leaf + swing arc
        let mut leaf = Line::new(
            Vector3::new(6000.0, 3500.0, 0.0),
            Vector3::new(7000.0, 3500.0, 0.0),
        );
        leaf.common.layer = "A-DOOR".into();
        doc.add_entity(leaf);

        let mut swing = Arc::new(Vector3::new(6000.0, 3500.0, 0.0), 1000.0, 0.0, 90.0);
        swing.common.layer = "A-DOOR".into();
        doc.add_entity(swing);

        // Room labels
        doc.add_entity(Text::new(
            "STUDIO",
            Vector3::new(2200.0, 4000.0, 0.0),
            350.0,
        ));
        doc.add_entity(Text::new(
            "WORKSHOP",
            Vector3::new(8200.0, 4000.0, 0.0),
            350.0,
        ));

        // Overall dimension
        let mut dim = Dimension::linear(
            Vector2::new(0.0, 0.0),
            Vector2::new(12000.0, 0.0),
            -800.0,
            0.0,
        );
        dim.common.layer = "A-DIMS".into();
        doc.add_entity(dim);

        doc
    }

    fn create_mechanical_part() -> DxfDocument {
        let mut doc = DxfDocument::new();
        // Outer flange circle R=50, inner bore R=20
        doc.add_entity(Circle::new(Vector3::ZERO, 50.0));
        doc.add_entity(Circle::new(Vector3::ZERO, 20.0));

        // 6 bolt holes on pitch circle R=35
        for i in 0..6 {
            let ang = (i as f64) * std::f64::consts::TAU / 6.0;
            doc.add_entity(Circle::new(
                Vector3::new(35.0 * ang.cos(), 35.0 * ang.sin(), 0.0),
                4.5,
            ));
        }

        // Centerlines
        doc.add_entity(Line::new(
            Vector3::new(-60.0, 0.0, 0.0),
            Vector3::new(60.0, 0.0, 0.0),
        ));
        doc.add_entity(Line::new(
            Vector3::new(0.0, -60.0, 0.0),
            Vector3::new(0.0, 60.0, 0.0),
        ));

        // Radial & linear dimensions
        doc.add_entity(Dimension::radial(
            Vector2::ZERO,
            Vector2::new(50.0, 0.0),
        ));
        doc.add_entity(Dimension::linear(
            Vector2::new(-50.0, -50.0),
            Vector2::new(50.0, -50.0),
            -15.0,
            0.0,
        ));
        doc.add_entity(Text::new(
            "FLANGE DN50",
            Vector3::new(-30.0, 62.0, 0.0),
            5.0,
        ));

        doc
    }

    fn create_electrical_schematic() -> DxfDocument {
        let mut doc = DxfDocument::new();
        // Main 400V busbar
        doc.add_entity(Line::new(
            Vector3::new(0.0, 100.0, 0.0),
            Vector3::new(200.0, 100.0, 0.0),
        ));
        // Three feeder branches
        for (idx, x) in [40.0, 100.0, 160.0].iter().enumerate() {
            doc.add_entity(Line::new(
                Vector3::new(*x, 100.0, 0.0),
                Vector3::new(*x, 40.0, 0.0),
            ));
            doc.add_entity(Circle::new(Vector3::new(*x, 30.0, 0.0), 10.0));
            doc.add_entity(Text::new(
                format!("M{}", idx + 1),
                Vector3::new(*x - 4.0, 27.0, 0.0),
                6.0,
            ));
        }
        doc
    }

    fn create_typography_sample() -> DxfDocument {
        let mut doc = DxfDocument::new();
        doc.add_entity(Text::new(
            "Single-Line CAD Text",
            Vector3::new(0.0, 20.0, 0.0),
            5.0,
        ));
        doc.add_entity(MText::new(
            "{\\C1;Red}\\P{\\C3;Green} & {\\H1.5x;Scaled}",
            Vector3::new(0.0, 0.0, 0.0),
            4.0,
        ));
        doc.add_entity(Line::new(
            Vector3::new(0.0, -10.0, 0.0),
            Vector3::new(80.0, -10.0, 0.0),
        ));
        doc
    }
}
