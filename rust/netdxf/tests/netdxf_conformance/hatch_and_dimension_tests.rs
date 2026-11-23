use netdxf::blocks::Block;
use netdxf::entities::{
    Dimension, DimensionBlock, EntityObject, Hatch, HatchBoundaryPath, HatchBoundaryPathEdge,
    HatchPattern, Insert, Line,
};
use netdxf::header::DxfVersion;
use netdxf::tables::DimensionStyle;
use netdxf::{DxfDocument, Vector2, Vector3};

#[test]
fn hatch_and_dimension_block_synthesis_conformance() {
    let bp = HatchBoundaryPath::new(vec![HatchBoundaryPathEdge::Polyline {
        vertexes: vec![
            (Vector2::new(0.0, 0.0), 0.0),
            (Vector2::new(10.0, 0.0), 0.0),
            (Vector2::new(10.0, 10.0), 0.0),
            (Vector2::new(0.0, 10.0), 0.0),
        ],
        is_closed: true,
    }]);
    let hatch = Hatch::new(HatchPattern::solid(), vec![bp]);
    let empty_hatch = Hatch::new(HatchPattern::new("ANSI31", "ANSI Iron"), Vec::new());

    let style = DimensionStyle::default_style();
    let dim_lin = Dimension::linear(Vector2::new(0.0, 0.0), Vector2::new(100.0, 0.0), 15.0, 0.0);
    let blk_lin = DimensionBlock::build(&dim_lin, &style, "*D1");
    assert!(!blk_lin.entities.is_empty());

    let dim_rad = Dimension::radial(Vector2::new(0.0, 0.0), Vector2::new(25.0, 0.0));
    let blk_rad = DimensionBlock::build(&dim_rad, &style, "*D2");
    assert!(!blk_rad.entities.is_empty());

    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    doc.blocks.add(blk_lin);
    doc.blocks.add(blk_rad);
    let mut grp_blk = Block::new("UNIT_BLK").unwrap();
    grp_blk.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
    doc.blocks.add(grp_blk);
    doc.add_entity(Insert::new("UNIT_BLK"));
    doc.add_entity(hatch);
    doc.add_entity(empty_hatch);
    doc.add_entity(dim_lin);
    doc.add_entity(dim_rad);

    for &binary in &[false, true] {
        let bytes = doc.save_to_bytes(binary).expect("save hatch/dim");
        let loaded = DxfDocument::load_from_bytes(&bytes).expect("load hatch/dim");
        let hatches = loaded
            .entities
            .iter()
            .filter(|e| matches!(e, EntityObject::Hatch(_)))
            .count();
        let dims = loaded
            .entities
            .iter()
            .filter(|e| matches!(e, EntityObject::Dimension(_)))
            .count();
        assert_eq!(hatches, 2);
        assert_eq!(dims, 2);
    }
}
