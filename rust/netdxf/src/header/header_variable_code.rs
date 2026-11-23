/// DXF header variable names (`$...`).
pub struct HeaderVariableCode;

impl HeaderVariableCode {
    pub const ACAD_VER: &'static str = "$ACADVER";
    pub const DWG_CODE_PAGE: &'static str = "$DWGCODEPAGE";
    pub const LAST_SAVED_BY: &'static str = "$LASTSAVEDBY";
    pub const HANDLE_SEED: &'static str = "$HANDSEED";
    pub const ANGBASE: &'static str = "$ANGBASE";
    pub const ANGDIR: &'static str = "$ANGDIR";
    pub const ATT_MODE: &'static str = "$ATTMODE";
    pub const A_UNITS: &'static str = "$AUNITS";
    pub const A_UPREC: &'static str = "$AUPREC";
    pub const CE_COLOR: &'static str = "$CECOLOR";
    pub const CE_LT_SCALE: &'static str = "$CELTSCALE";
    pub const CE_LTYPE: &'static str = "$CELTYPE";
    pub const CE_LWEIGHT: &'static str = "$CELWEIGHT";
    pub const C_LAYER: &'static str = "$CLAYER";
    pub const CML_JUST: &'static str = "$CMLJUST";
    pub const CML_SCALE: &'static str = "$CMLSCALE";
    pub const CML_STYLE: &'static str = "$CMLSTYLE";
    pub const DIM_STYLE: &'static str = "$DIMSTYLE";
    pub const TEXT_SIZE: &'static str = "$TEXTSIZE";
    pub const TEXT_STYLE: &'static str = "$TEXTSTYLE";
    pub const L_UNITS: &'static str = "$LUNITS";
    pub const L_UPREC: &'static str = "$LUPREC";
    pub const MIRR_TEXT: &'static str = "$MIRRTEXT";
    pub const EXTNAMES: &'static str = "$EXTNAMES";
    pub const INS_BASE: &'static str = "$INSBASE";
    pub const INS_UNITS: &'static str = "$INSUNITS";
    pub const LT_SCALE: &'static str = "$LTSCALE";
    pub const LW_DISPLAY: &'static str = "$LWDISPLAY";
    pub const PD_MODE: &'static str = "$PDMODE";
    pub const PD_SIZE: &'static str = "$PDSIZE";
    pub const P_LINE_GEN: &'static str = "$PLINEGEN";
    pub const PS_LT_SCALE: &'static str = "$PSLTSCALE";
    pub const SPLINE_SEGS: &'static str = "$SPLINESEGS";
    pub const SURF_U: &'static str = "$SURFU";
    pub const SURF_V: &'static str = "$SURFV";
    pub const TD_CREATE: &'static str = "$TDCREATE";
    pub const TDU_CREATE: &'static str = "$TDUCREATE";
    pub const TD_UPDATE: &'static str = "$TDUPDATE";
    pub const TDU_UPDATE: &'static str = "$TDUUPDATE";
    pub const TDIN_DWG: &'static str = "$TDINDWG";
    pub const UCS_ORG: &'static str = "$UCSORG";
    pub const UCS_X_DIR: &'static str = "$UCSXDIR";
    pub const UCS_Y_DIR: &'static str = "$UCSYDIR";
}
