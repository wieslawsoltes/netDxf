// The stream adapter follows the pinned BinaryWriter character-array framing.
// .NET Foundation and Contributors, MIT; see DOTNET-MIT-LICENSE.txt.
// https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs
// Character arrays have no length prefix. Each completed encoded chunk is written
// before the next one is encoded, preserving stream/encoder failure ordering.
export function WriteBinaryCharacters(writer, encoding, text) {
  const utf8 = encoding.CodePage === 65001;
  const limit = 65536;
  if (!utf8 || text.length <= 21844) { writer.Write(encoding.GetBytes(text)); return; }
  let start = 0;
  while (start < text.length) {
    let end = start, bytes = 0;
    while (end < text.length) {
      const code = text.charCodeAt(end), next = text.charCodeAt(end + 1);
      const pair = code >= 0xd800 && code <= 0xdbff && next >= 0xdc00 && next <= 0xdfff;
      // Encoder.Convert validates an invalid scalar before checking output
      // capacity, even when that scalar is immediately beyond a full chunk.
      if (!pair && code >= 0xd800 && code <= 0xdfff) encoding.GetBytes(text[end]);
      const width = pair ? 4 : code < 0x80 ? 1 : code < 0x800 ? 2 : 3;
      if (bytes + width > limit) break;
      bytes += width; end += pair ? 2 : 1;
    }
    const buffer = encoding.GetBytes(text.slice(start, end));
    if (buffer.length) writer.Write(buffer);
    start = end;
  }
}
