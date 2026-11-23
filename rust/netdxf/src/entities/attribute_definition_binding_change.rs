/// Describes an explicit binding change between an `Attribute` and an `AttributeDefinition`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AttributeDefinitionBindingChange {
    pub previous_tag: Option<String>,
    pub new_tag: Option<String>,
    pub preserve_value: bool,
}

impl AttributeDefinitionBindingChange {
    pub fn apply(&self) -> Option<&str> {
        self.new_tag.as_deref()
    }

    pub fn rollback(&self) -> Option<&str> {
        self.previous_tag.as_deref()
    }
}
