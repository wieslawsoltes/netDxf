# LIGHT name transport integrity

On the merged PR78 baseline (`9a11080cff4e33b2260e52c3c18fe60ad03d44b2`), `Light.Name` admitted CR, LF and NUL. These delimiters are incompatible with the line-oriented text and NUL-terminated binary transports. Also, literal `\U+####` name text was decoded as a Unicode escape after save/reload.

The setter now rejects null and CR/LF/NUL before mutation. The reader retains its contextual InvalidDataException / Release null convention for invalid decoded names. The LIGHT writer encodes literal backslashes as `\U+005C` before ordinary Unicode encoding. The single-pass decoder restores them without recursively decoding the restored text. Empty names, tabs, valid Unicode, backslash paths, literal uppercase/lowercase Unicode-escape spelling and long strings retain their existing accepted model behavior. This does not impose a new global DXF string-size policy or alter any other entity's encoding.

21 registered cases supplement the existing suite: setter nonmutation, encoded delimiters in six admitted typed profiles and both transports, and valid persistence in all four LIGHT-export profiles and both transports. The same initial tests on unchanged production report **17,145 passed / 21 failed** in Debug and Release. The corrected tests (also extended with more literal-path/escape cases within the existing registrations) report **17,166 passed / zero failed**, both configurations on the separately compiled signed .NET8 library. CI must validate the final PR head on Linux/Windows Debug/Release and build netstandard2.0 before merge.

No historical LIGHT admission is added. Pre-2007 export is still rejected. The public API is unchanged; the setter strengthens validation and the writer preserves literal name semantics. Native AutoCAD was not executed.

Reference: [Autodesk LIGHT](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-1A23DB42-6A92-48E9-9EB2-A7856A479930.htm), group 1, and the library's existing line/NUL-framed codecs and single-pass escape decoder. General formatting-bearing MTEXT and text-value schemas remain separate work.
