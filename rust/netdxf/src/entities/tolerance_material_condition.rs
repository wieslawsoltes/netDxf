/// Defines the material condition for a tolerance or datum reference.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum ToleranceMaterialCondition {
    #[default]
    None,
    Maximum,
    Least,
    Regardless,
}
