use bitflags::bitflags;

bitflags! {
    /// Controls which layer properties are restored from a `LayerState`.
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct LayerPropertiesRestoreFlags: i32 {
        const NONE = 0;
        const HIDDEN = 1;
        const FROZEN = 2;
        const LOCKED = 4;
        const PLOT = 8;
        const NEW_VP_FROZEN = 16;
        const VP_FROZEN = 32;
        const COLOR = 64;
        const LINETYPE = 128;
        const LINEWEIGHT = 256;
        const TRANSPARENCY = 512;
        const PLOT_STYLE = 1024;
        const ALL = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 256 | 512 | 1024;
    }
}
