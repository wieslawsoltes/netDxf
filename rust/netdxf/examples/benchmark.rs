use std::time::Instant;

use netdxf::entities::{Arc, Circle, Line, Polyline2D, Polyline2DVertex};
use netdxf::{DxfDocument, Vector3};

fn main() {
    const COUNT: usize = 20_000;
    let t0 = Instant::now();
    let mut doc = DxfDocument::new();
    for i in 0..COUNT {
        let x = (i % 200) as f64 * 10.0;
        let y = (i / 200) as f64 * 10.0;
        match i % 4 {
            0 => doc.add_entity(Line::new(
                Vector3::new(x, y, 0.0),
                Vector3::new(x + 8.0, y + 6.0, 0.0),
            )),
            1 => doc.add_entity(Circle::new(Vector3::new(x, y, 0.0), 4.0)),
            2 => doc.add_entity(Arc::new(Vector3::new(x, y, 0.0), 4.0, 0.0, 180.0)),
            _ => doc.add_entity(Polyline2D::new(
                vec![
                    Polyline2DVertex::new(x, y),
                    Polyline2DVertex::new(x + 5.0, y),
                    Polyline2DVertex::new(x + 5.0, y + 5.0),
                ],
                true,
            )),
        }
    }
    let build_ms = t0.elapsed().as_secs_f64() * 1000.0;

    let t1 = Instant::now();
    let ascii = doc.save_to_bytes(false).unwrap();
    let write_ascii_ms = t1.elapsed().as_secs_f64() * 1000.0;

    let t2 = Instant::now();
    let loaded_ascii = DxfDocument::load_from_bytes(&ascii).unwrap();
    let read_ascii_ms = t2.elapsed().as_secs_f64() * 1000.0;
    assert_eq!(loaded_ascii.entities.len(), COUNT);

    let t3 = Instant::now();
    let binary = doc.save_to_bytes(true).unwrap();
    let write_bin_ms = t3.elapsed().as_secs_f64() * 1000.0;

    let t4 = Instant::now();
    let loaded_bin = DxfDocument::load_from_bytes(&binary).unwrap();
    let read_bin_ms = t4.elapsed().as_secs_f64() * 1000.0;
    assert_eq!(loaded_bin.entities.len(), COUNT);

    println!("=== netdxf Rust Benchmark ({COUNT} entities) ===");
    println!("Build:        {build_ms:7.2} ms");
    println!(
        "Write ASCII:  {write_ascii_ms:7.2} ms ({:.2} MB)",
        ascii.len() as f64 / (1024.0 * 1024.0)
    );
    println!("Read ASCII:   {read_ascii_ms:7.2} ms");
    println!(
        "Write Binary: {write_bin_ms:7.2} ms ({:.2} MB)",
        binary.len() as f64 / (1024.0 * 1024.0)
    );
    println!("Read Binary:  {read_bin_ms:7.2} ms");
}
