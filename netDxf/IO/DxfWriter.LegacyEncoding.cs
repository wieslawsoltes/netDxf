using System;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private Encoding PrepareOutputEncoding()
        {
            if (this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
                return new UTF8Encoding(false);

            // ASCII bytes have the same representation in every supported legacy
            // codepage. Keep ordinary authored drawings independent of optional
            // runtime codepage providers when no opaque Unicode text needs one.
            var objects = this.doc.Objects.Items.OfType<DxfOpaqueObject>().ToList();
            if (!objects.Any(opaque => opaque.Tags.Any(tag => tag.Value is string text && text.Any(character => character > 127))))
                return new UTF8Encoding(false);

            // Legacy readers decode unescaped text using the declared codepage.
            // Known text fields are already escaped by their schema writers;
            // opaque strings must retain their exact spelling, including literal
            // Unicode-looking escapes, without interpreting their private meaning.
            Encoding encoding = DxfRawDocument.ResolveEncoding(this.doc.DrawingVariables.AcadVer,
                this.doc.DrawingVariables.DwgCodePage);
            foreach (DxfOpaqueObject opaque in objects)
            {
                foreach (DxfTag tag in opaque.Tags)
                {
                    if (!(tag.Value is string text)) continue;
                    try { encoding.GetByteCount(text); }
                    catch (EncoderFallbackException error)
                    {
                        throw new NotSupportedException("Retained OBJECTS text cannot be represented in the declared legacy codepage. "
                            + "Choose AutoCAD 2007 or later to preserve the literal text with UTF-8.", error);
                    }
                }
            }
            return encoding;
        }
    }
}
