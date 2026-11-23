use netdxf::header::{HeaderVariable, HeaderVariableValue};
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn custom_header_unicode_and_scalars_roundtrip() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let expected_str = if version.uses_utf8() {
                "Zażółć 東京 Ω"
            } else {
                "ordinary ASCII \\ path"
            };
            doc.drawing_variables.custom_variables.insert(
                "$PROJECTNAME".into(),
                HeaderVariable::new(
                    "$PROJECTNAME",
                    1,
                    HeaderVariableValue::String(expected_str.into()),
                ),
            );
            doc.drawing_variables.custom_variables.insert(
                "$USERI1".into(),
                HeaderVariable::new("$USERI1", 70, HeaderVariableValue::Int16(-123)),
            );
            doc.drawing_variables.custom_variables.insert(
                "$USERR1".into(),
                HeaderVariable::new("$USERR1", 40, HeaderVariableValue::Float(1.23456789)),
            );
            doc.drawing_variables.custom_variables.insert(
                "$EXTMIN".into(),
                HeaderVariable::new(
                    "$EXTMIN",
                    30,
                    HeaderVariableValue::Vector3(Vector3::new(-10.0, -20.0, -30.0)),
                ),
            );

            let bytes = doc.save_to_bytes(binary).expect("save custom header");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load custom header");
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$PROJECTNAME")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::String(expected_str.into()))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$USERI1")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Int16(-123))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$USERR1")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Float(1.23456789))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$EXTMIN")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Vector3(Vector3::new(
                    -10.0, -20.0, -30.0
                )))
            );
        }
    }
}
