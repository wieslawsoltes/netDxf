/// The primitive value representation of a modern DXF group code.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum DxfTagValueType {
    String,
    Double,
    Int16,
    Int32,
    Int64,
    Boolean,
    BinaryData,
    Handle,
}

/// Reference semantics associated with a DXF handle group code.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum DxfHandleKind {
    None,
    ObjectIdentity,
    Arbitrary,
    SoftPointer,
    HardPointer,
    SoftOwner,
    HardOwner,
    XData,
}

/// Primitive encoding metadata for DXF group codes (`netDxf.IO.DxfGroupCode`).
pub struct DxfGroupCode;

impl DxfGroupCode {
    #[inline]
    pub const fn try_get_value_type(code: i16) -> Option<DxfTagValueType> {
        if !matches!(Self::get_handle_kind(code), DxfHandleKind::None) {
            return Some(DxfTagValueType::Handle);
        }
        match code {
            0..=9
            | 100..=102
            | 300..=309
            | 410..=419
            | 430..=439
            | 470..=479
            | 999
            | 1000..=1003
            | 1006..=1009 => Some(DxfTagValueType::String),
            10..=59 | 110..=149 | 210..=239 | 460..=469 | 1010..=1059 => {
                Some(DxfTagValueType::Double)
            }
            60..=79 | 170..=179 | 270..=289 | 370..=389 | 400..=409 | 1060..=1070 => {
                Some(DxfTagValueType::Int16)
            }
            90..=99 | 420..=429 | 440..=459 | 1071 => Some(DxfTagValueType::Int32),
            160..=169 => Some(DxfTagValueType::Int64),
            290..=299 => Some(DxfTagValueType::Boolean),
            310..=319 | 1004 => Some(DxfTagValueType::BinaryData),
            _ => None,
        }
    }

    #[inline]
    pub const fn get_handle_kind(code: i16) -> DxfHandleKind {
        match code {
            5 | 105 => DxfHandleKind::ObjectIdentity,
            320..=329 => DxfHandleKind::Arbitrary,
            330..=339 => DxfHandleKind::SoftPointer,
            340..=349 | 390..=399 | 480 | 481 => DxfHandleKind::HardPointer,
            350..=359 => DxfHandleKind::SoftOwner,
            360..=369 => DxfHandleKind::HardOwner,
            1005 => DxfHandleKind::XData,
            _ => DxfHandleKind::None,
        }
    }
}
