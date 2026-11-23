use crate::DimensionSnapshotReader;
use netdxf::entities::DimensionBlock;
use netdxf::DxfDocument;
use netdxf_shadow::{ObjectId, ShadowSnapshot};
use netdxf_shadow_geometry::{
    CadComponent, CadComponentStyle, CadGeometry, CadSceneBuilder, CadStyleSource,
    ICacheableEntityProjector, IEntityProjector, SceneOptions,
};
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DimensionProjectionOptions {
    pub maximum_source_objects: usize,
    pub maximum_source_values: usize,
    pub maximum_depth: usize,
    pub maximum_generated_occurrences: usize,
    pub maximum_text_length: usize,
    pub culture_name: String,
    pub regenerate_missing_blocks: bool,
}

impl Default for DimensionProjectionOptions {
    fn default() -> Self {
        Self {
            maximum_source_objects: 4_096,
            maximum_source_values: 131_072,
            maximum_depth: 16,
            maximum_generated_occurrences: 16_384,
            maximum_text_length: 1_024,
            culture_name: "en-US".into(),
            regenerate_missing_blocks: true,
        }
    }
}

/// Projects DXF dimension entities into [`CadGeometry`] using existing or freshly synthesized
/// anonymous `*D` dimension block definitions (`netDxf.Shadow.Dimensions.DimensionEntityProjector`).
#[derive(Debug, Clone, Default)]
pub struct DimensionEntityProjector {
    pub options: DimensionProjectionOptions,
}

impl DimensionEntityProjector {
    pub fn new(options: DimensionProjectionOptions) -> Self {
        Self { options }
    }

    #[inline]
    pub fn revision(&self) -> i64 {
        1
    }

    pub fn try_project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        self.project(snapshot, entity_id)
    }
}

impl IEntityProjector for DimensionEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        if !reader.exists() || !DimensionSnapshotReader::is_dimension_type(reader.type_name()) {
            return None;
        }

        // First: if the snapshot already has a populated Block reference, project its entities directly.
        if let Some(block_id) = reader.field("Block").id() {
            let block_reader = snapshot.read(block_id.clone());
            let block_entities = block_reader.field("Entities").items();
            if !block_entities.is_empty() {
                let mut builder = CadSceneBuilder::new();
                let mut components = Vec::new();
                for (idx, ent) in block_entities
                    .iter()
                    .take(self.options.maximum_generated_occurrences)
                    .enumerate()
                {
                    if let Some(child_id) = ent.id() {
                        let scene = builder.build(
                            snapshot,
                            &SceneOptions {
                                root_entities: vec![child_id.clone()],
                                maximum_depth: self.options.maximum_depth,
                                maximum_occurrences: 64,
                                ..Default::default()
                            },
                        );
                        for occ in scene.occurrences {
                            components.push(CadComponent {
                                key: format!("{}:block:{idx}", entity_id.0),
                                geometry: occ.geometry,
                                style: Some(CadComponentStyle {
                                    value: occ.style,
                                    color: CadStyleSource::Explicit,
                                    lineweight: CadStyleSource::Explicit,
                                    linetype: CadStyleSource::Explicit,
                                    alpha: None,
                                    dashes: Vec::new(),
                                    dash_scale: 1.0,
                                    inherit_layer: false,
                                }),
                            });
                        }
                    }
                }
                if !components.is_empty() {
                    return Some(CadGeometry {
                        components,
                        ..Default::default()
                    });
                }
            }
        }

        // Second: if regenerate_missing_blocks is enabled, rehydrate the Dimension + DimensionStyle,
        // synthesize its anonymous block with `DimensionBlock::build`, capture via `ShadowSession`,
        // and project into `CadComponent`s.
        if !self.options.regenerate_missing_blocks {
            return Some(CadGeometry::default());
        }

        let (dim, style) = DimensionSnapshotReader::read(snapshot, &reader, &self.options)?;
        let block = DimensionBlock::build(&dim, &style, "*D_REGEN");
        let mut temp_doc = DxfDocument::new();
        for ent in block.entities {
            temp_doc.add_entity(ent);
        }

        let captured = ShadowSession::capture(
            &temp_doc,
            format!("dim:{}", entity_id.0),
            ShadowCaptureOptions::default(),
        );
        let mut builder = CadSceneBuilder::new();
        let scene = builder.build(
            &captured,
            &SceneOptions {
                maximum_depth: self.options.maximum_depth,
                maximum_occurrences: self.options.maximum_generated_occurrences,
                ..Default::default()
            },
        );

        let mut out = CadGeometry::default();
        for (idx, occ) in scene.occurrences.into_iter().enumerate() {
            out.contours.extend(occ.geometry.contours.clone());
            out.texts.extend(occ.geometry.texts.clone());
            out.points.extend(occ.geometry.points.clone());
            out.components.push(CadComponent {
                key: format!("{}:regen:{idx}", entity_id.0),
                geometry: occ.geometry,
                style: Some(CadComponentStyle {
                    value: occ.style,
                    color: CadStyleSource::Explicit,
                    lineweight: CadStyleSource::Explicit,
                    linetype: CadStyleSource::Explicit,
                    alpha: None,
                    dashes: Vec::new(),
                    dash_scale: 1.0,
                    inherit_layer: false,
                }),
            });
        }
        Some(out)
    }
}

impl ICacheableEntityProjector for DimensionEntityProjector {
    fn projector_key(&self) -> &str {
        "dimensions"
    }
}
