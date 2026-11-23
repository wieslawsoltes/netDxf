use netdxf_shadow::ObjectId;

/// Resolves live source objects by [`ObjectId`].
pub trait IShadowObjectResolver {
    fn contains(&self, id: &ObjectId) -> bool;
}

/// Factory helpers for creating `netdxf` entities/table objects from shadow descriptors.
pub struct ShadowObjectFactories;

impl ShadowObjectFactories {
    pub fn is_supported_entity_type(type_name: &str) -> bool {
        matches!(
            type_name,
            "netDxf.Entities.Line"
                | "netDxf.Entities.Circle"
                | "netDxf.Entities.Arc"
                | "netDxf.Entities.Point"
                | "netDxf.Entities.Polyline2D"
                | "netDxf.Entities.Text"
                | "netDxf.Entities.MText"
                | "netDxf.Entities.Insert"
        )
    }
}
