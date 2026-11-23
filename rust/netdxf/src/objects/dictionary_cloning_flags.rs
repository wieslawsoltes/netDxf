/// Duplicate record cloning flags for DXF `DICTIONARY` objects (group 281).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum DictionaryCloningFlags {
    #[default]
    NotApplicable = 0,
    KeepExisting = 1,
    UseClone = 2,
    XrefName = 3,
    Name = 4,
    UnmangleName = 5,
}
