/// Represents a DXF `CLASS` entry in the `CLASSES` section.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DxfClass {
    pub dxf_name: String,
    pub cpp_class_name: String,
    pub application_name: String,
    pub proxy_capabilities: i32,
    pub instance_count: i32,
    pub was_a_proxy: bool,
    pub is_an_entity: bool,
}

impl DxfClass {
    pub fn new(
        dxf_name: impl Into<String>,
        cpp_class_name: impl Into<String>,
        application_name: impl Into<String>,
    ) -> Self {
        Self {
            dxf_name: dxf_name.into(),
            cpp_class_name: cpp_class_name.into(),
            application_name: application_name.into(),
            proxy_capabilities: 0,
            instance_count: 0,
            was_a_proxy: false,
            is_an_entity: false,
        }
    }

    #[inline] pub fn name(&self) -> &str { &self.dxf_name }
    #[inline] pub fn proxy_flags(&self) -> i32 { self.proxy_capabilities }
    #[inline] pub fn was_proxy(&self) -> bool { self.was_a_proxy }
    #[inline] pub fn is_entity(&self) -> bool { self.is_an_entity }
}
