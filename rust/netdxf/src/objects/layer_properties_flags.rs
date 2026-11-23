use bitflags::bitflags;

bitflags! {
    /// Layer state property flags (DXF group 90 in `AcDbLayerState`).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct LayerPropertiesFlags: i32 {
        const NONE = 0;
        const HIDDEN = 1;
        const FROZEN = 2;
        const LOCKED = 4;
        const PLOT = 8;
        const NEW_VP_FROZEN = 16;
        const VP_FROZEN = 32;
    }
}
