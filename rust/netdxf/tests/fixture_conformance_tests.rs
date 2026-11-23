use std::path::PathBuf;

use netdxf::header::DxfVersion;
use netdxf::DxfDocument;

fn repo_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .to_path_buf()
}

#[test]
fn load_sample_ascii_dxf() {
    let path = repo_root().join("TestDxfDocument").join("sample.dxf");
    if !path.exists() {
        return;
    }
    let (ver, is_bin) = DxfDocument::check_dxf_file_version(&path).unwrap();
    assert!(!is_bin);
    assert_ne!(ver, DxfVersion::Unknown);

    let mut doc = DxfDocument::load(&path).expect("failed to load sample.dxf");
    assert!(!doc.entities.is_empty(), "sample.dxf should contain entities");
    assert!(!doc.layers.is_empty(), "sample.dxf should contain layers");

    // Round-trip sample.dxf to both ASCII and Binary in memory
    let ascii = doc.save_to_bytes(false).expect("failed to save sample as ASCII");
    let reloaded = DxfDocument::load_from_bytes(&ascii).expect("failed to reload sample ASCII");
    assert_eq!(reloaded.entities.len(), doc.entities.len());

    let binary = doc.save_to_bytes(true).expect("failed to save sample as Binary");
    let reloaded_bin = DxfDocument::load_from_bytes(&binary).expect("failed to reload sample Binary");
    assert_eq!(reloaded_bin.entities.len(), doc.entities.len());
}

#[test]
fn load_sample_binary_dxf() {
    let path = repo_root().join("TestDxfDocument").join("sample binary.dxf");
    if !path.exists() {
        return;
    }
    let (ver, is_bin) = DxfDocument::check_dxf_file_version(&path).unwrap();
    assert!(is_bin);
    assert_ne!(ver, DxfVersion::Unknown);

    let doc = DxfDocument::load(&path).expect("failed to load sample binary.dxf");
    assert!(!doc.entities.is_empty(), "sample binary.dxf should contain entities");
}

#[test]
fn load_all_conformance_dxf_fixtures() {
    let fixtures_dir = repo_root().join("tests").join("fixtures");
    if !fixtures_dir.exists() {
        return;
    }

    let mut dxf_files = Vec::new();
    collect_dxf_files(&fixtures_dir, &mut dxf_files);
    assert!(!dxf_files.is_empty(), "expected DXF fixtures under tests/fixtures");

    let mut loaded_count = 0usize;
    for path in &dxf_files {
        // Some negative conformance fixtures intentionally contain corrupt/truncated DXF;
        // verify we never panic on any fixture and successfully load valid ones.
        if let Ok(doc) = DxfDocument::load(path) {
            loaded_count += 1;
            let _ = doc.entities.len();
        }
    }
    assert!(
        loaded_count >= 10,
        "expected at least 10 valid DXF fixtures to load cleanly, got {loaded_count} of {}",
        dxf_files.len()
    );
}

fn collect_dxf_files(dir: &std::path::Path, out: &mut Vec<PathBuf>) {
    if let Ok(entries) = std::fs::read_dir(dir) {
        for entry in entries.flatten() {
            let p = entry.path();
            if p.is_dir() {
                collect_dxf_files(&p, out);
            } else if p
                .extension()
                .and_then(|e| e.to_str())
                .is_some_and(|e| e.eq_ignore_ascii_case("dxf"))
            {
                out.push(p);
            }
        }
    }
}
