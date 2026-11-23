use std::env;
use std::path::Path;

use netdxf::blocks::Block;
use netdxf::entities::*;
use netdxf::header::DxfVersion;
use netdxf::tables::{ApplicationRegistry, Layer};
use netdxf::{AciColor, DxfDocument, Vector3, XData, XDataCode, XDataRecord, XDataValue};

fn build_doc(version: DxfVersion) -> DxfDocument {
    let mut doc = DxfDocument::with_version(version);
    let mut layer = Layer::new("Geometry").unwrap();
    layer.color = AciColor::from_rgb(30, 144, 255);
    doc.layers.add(layer);
    doc.application_registries
        .add(ApplicationRegistry::new("RUST_INTEROP").unwrap());

    let mut line = Line::new(Vector3::ZERO, Vector3::new(100.0, 50.0, 10.0));
    line.common.layer = "Geometry".into();
    let mut xd = XData::new("RUST_INTEROP");
    xd.x_data_record.push(XDataRecord::string("from-rust"));
    xd.x_data_record.push(XDataRecord::new(
        XDataCode::BinaryData,
        XDataValue::Binary(vec![0xDE, 0xAD, 0xBE, 0xEF]),
    ));
    line.common.object.add_xdata(xd);
    doc.add_entity(line);

    doc.add_entity(Circle::new(Vector3::new(10.0, 20.0, 0.0), 5.0));
    doc.add_entity(Arc::new(Vector3::new(5.0, 5.0, 0.0), 3.0, 15.0, 195.0));
    doc.add_entity(MText::new(
        "Hello from Rust 東京 Zażółć",
        Vector3::new(0.0, 10.0, 0.0),
        2.5,
    ));
    doc.add_entity(Polyline2D::new(
        vec![
            Polyline2DVertex::new(0.0, 0.0),
            Polyline2DVertex::with_bulge(10.0, 0.0, 0.5),
            Polyline2DVertex::new(10.0, 10.0),
        ],
        true,
    ));
    doc.add_entity(Spline::from_control_points(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(5.0, 10.0, 0.0),
            Vector3::new(10.0, 0.0, 0.0),
            Vector3::new(15.0, 10.0, 0.0),
        ],
        3,
        false,
    ));

    let mut blk = Block::new("RustBlock").unwrap();
    blk.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
    doc.blocks.add(blk);
    doc.add_entity(Insert::new("RustBlock"));

    if version <= DxfVersion::AutoCad2010 {
        let mut solid = Solid3D::new();
        solid
            .acis
            .set_sat_lines(["700 0 1 0", "End-of-ACIS-data"])
            .unwrap();
        doc.add_entity(solid);
    }
    doc
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let mode = args.get(1).map(String::as_str).unwrap_or("write");
    let dir = Path::new(args.get(2).map(String::as_str).unwrap_or("/tmp/netdxf_interop"));
    std::fs::create_dir_all(dir).unwrap();

    if mode == "write" {
        for (name, ver, bin) in [
            ("rust_r2018_ascii.dxf", DxfVersion::AutoCad2018, false),
            ("rust_r2018_binary.dxf", DxfVersion::AutoCad2018, true),
            ("rust_r2010_ascii.dxf", DxfVersion::AutoCad2010, false),
            ("rust_r2010_binary.dxf", DxfVersion::AutoCad2010, true),
        ] {
            let mut doc = build_doc(ver);
            let bytes = doc.save_to_bytes(bin).expect("Rust save failed");
            std::fs::write(dir.join(name), bytes).expect("write failed");
        }
        println!("Rust wrote 4 DXF files to {}", dir.display());
    } else if mode == "read" {
        for name in ["csharp_r2018_ascii.dxf", "csharp_r2018_binary.dxf"] {
            let doc = DxfDocument::load(dir.join(name)).expect("Rust load of C# DXF failed");
            let line = doc.entities.lines().next().expect("C# line");
            assert!((line.start_point - Vector3::new(1.5, -2.5, 3.5)).modulus() < 1e-9);
            assert!((line.end_point - Vector3::new(10.5, 20.5, -30.5)).modulus() < 1e-9);
            assert_eq!(line.common.color.to_rgb(), [18, 171, 52]);
            let xd = line.common.object.get_xdata("CS_INTEROP").expect("C# XData");
            assert_eq!(xd.x_data_record[0].value, XDataValue::String("from-csharp".into()));
            assert_eq!(
                xd.x_data_record[1].value,
                XDataValue::Binary(vec![0xCA, 0xFE, 0xBA, 0xBE])
            );
            let circle = doc.entities.circles().next().expect("C# circle");
            assert!((circle.radius - 12.25).abs() < 1e-9);
            let mt = doc.entities.mtexts().next().expect("C# MText");
            assert!(mt.value.contains("東京") && mt.value.contains("Zażółć"));
            println!("Rust verified C# file {name}: OK");
        }
    }
}
