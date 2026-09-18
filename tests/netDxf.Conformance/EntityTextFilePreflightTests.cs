// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterEntityTextFilePreflightTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            foreach (string kind in TextWireKinds) foreach (bool existing in new[] { false, true })
                foreach (bool atomic in new[] { false, true })
                {
                    string[] samples = binary ? new[] { "bad\0tail", "bad\ud800tail", "bad\udc00tail" } :
                        new[] { "bad\0tail", "bad\ud800tail", "bad\udc00tail", "bad\r\ntail" };
                    for (int i = 0; i < samples.Length; i++)
                    {
                        string value = samples[i];
                        Run($"entity-text-file/{version}/{binary}/{kind}/{existing}/{atomic}/{i}", () =>
                            EntityTextFilePreflight(version, binary, kind, existing, atomic, value));
                    }
                }
    }

    private static void EntityTextFilePreflight(DxfVersion version, bool binary, string kind, bool existing, bool atomic, string value)
        => WithAtomicDirectory(path =>
        {
            AtomicPrepare(path, existing);
            var test = TextWireDocument(version, kind, true); var doc = test.Document;
            doc.Name = "original document name";
            string folder = doc.SupportFolders.WorkingFolder;
            var objects = doc.Objects.Items.ToArray(); var blocks = doc.Blocks.ToArray();
            var layouts = doc.Layouts.ToArray(); var registries = doc.ApplicationRegistries.Items.ToArray();
            test.Set(value);
            if (atomic) Throws<InvalidDataException>(() => doc.SaveAtomic(path, binary));
            else
            {
#if DEBUG
                Throws<InvalidDataException>(() => doc.Save(path, binary));
#else
                Check(!doc.Save(path, binary), "Conventional file save accepted malformed text");
#endif
            }
            AtomicUnchanged(path, existing);
            Equal("original document name", doc.Name, "Rejected file save changed name");
            Equal(folder, doc.SupportFolders.WorkingFolder, "Rejected file save changed working folder");
            Equal(value, test.Get(), "Rejected file save changed source content");
            Check(objects.SequenceEqual(doc.Objects.Items) && blocks.SequenceEqual(doc.Blocks), "Rejection changed objects/blocks");
            Check(layouts.SequenceEqual(doc.Layouts) && registries.SequenceEqual(doc.ApplicationRegistries.Items), "Rejection changed layouts/registries");
            test.Set("repaired");
            if (atomic) doc.SaveAtomic(path, binary);
            else Check(doc.Save(path, binary), "Repaired conventional save failed");
            Equal(Path.GetFileNameWithoutExtension(path), doc.Name, "Successful save name semantics changed");
            Equal(Path.GetDirectoryName(path), doc.SupportFolders.WorkingFolder, "Successful save folder semantics changed");
            using (var source = File.OpenRead(path))
                Equal(version, DxfRawDocument.Load(source).Version, "Repaired file profile changed");
            CheckLifetimeFileReleased(path);
        });
}
