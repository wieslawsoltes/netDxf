using netDxf.Collections;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateTextStyleStrings()
        {
            foreach (var style in this.doc.TextStyles.Items)
            {
                CheckStyleUnicode(style.Name);
                CheckStyleUnicode(style.FontFile);
                CheckStyleUnicode(style.BigFont);
                CheckStyleXDataUnicode(style.XData);
            }
            foreach (var style in this.doc.ShapeStyles.Items)
            {
                CheckStyleUnicode(style.Name);
                CheckStyleUnicode(style.File);
                CheckStyleXDataUnicode(style.XData);
            }
        }

        private static void CheckStyleXDataUnicode(XDataDictionary data)
        {
            foreach (var item in data.Values)
            {
                CheckStyleUnicode(item.ApplicationRegistry.Name);
                foreach (var record in item.XDataRecord)
                    if (record.Value is string value) CheckStyleUnicode(value);
            }
        }

        private static void CheckStyleUnicode(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsSurrogate(c)) continue;
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
                throw new System.IO.InvalidDataException("STYLE strings cannot contain unpaired UTF-16 surrogates.");
            }
        }

        private string EncodeStyleString(string value)
        {
            return this.EncodeDatabaseString(value).Replace("\0", "\\U+0000").Replace("\r", "\\U+000D").Replace("\n", "\\U+000A");
        }

        private void WriteStyleXData(XDataDictionary data)
        {
            foreach (string appId in data.AppIds)
                this.WriteXDataRecords(appId, data[appId].XDataRecord, true);
        }
    }
}
