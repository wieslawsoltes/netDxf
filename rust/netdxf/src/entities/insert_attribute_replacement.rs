use crate::entities::Attribute;

/// Describes a replacement attribute operation on an `Insert` entity.
#[derive(Debug, Clone, PartialEq)]
pub struct InsertAttributeReplacement {
    pub tag: String,
    pub replacement: Attribute,
}

impl InsertAttributeReplacement {
    pub fn attributes(&self) -> std::slice::Iter<'_, Attribute> {
        std::slice::from_ref(&self.replacement).iter()
    }

    pub fn apply(&self, attributes: &mut Vec<Attribute>) {
        if let Some(existing) = attributes
            .iter_mut()
            .find(|a| a.tag.eq_ignore_ascii_case(&self.tag))
        {
            *existing = self.replacement.clone();
        } else {
            attributes.push(self.replacement.clone());
        }
    }
}
