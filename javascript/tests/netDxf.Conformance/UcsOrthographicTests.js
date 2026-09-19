// Complete original detached model test; typed DXF tests remain unregistered.
import { UCS,Vector3,UcsOrthographicType } from '../../index.js';
import { NotSupportedException,ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run,Check,Equal,Near,Throws } from './TestHarness.js';
export function RegisterUcsOrthographicTests() { Run('ucs/orthographic/model',UcsOrthographicModelTests); }
    export function UcsOrthographicModelTests()
    {
        for (const ucs of [ new UCS("Default"), new UCS("Axes", new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitY),
            UCS.FromNormal("Normal", Vector3.Zero, Vector3.UnitZ) ])
        {
            Equal(0, ucs.OrthographicOrigins.Count, "new UCS invented overrides");
            Check(ucs.OrthographicOrigins === ucs.OrthographicOrigins, "Read-only view is not stable.");
            for (let type = 1; type <= 6; type++)
            {
                const key = type;
                const box = {}; Check(!ucs.TryGetOrthographicOrigin(key, box) && Vector3.Equals(box.value, Vector3.Zero), "Missing override was invented.");
                ucs.SetOrthographicOrigin(key, OrthoPoint(type));
                Check(ucs.TryGetOrthographicOrigin(key, box), "Stored override missing.");
                Equal(OrthoPoint(type), box.value, "override value");
                ucs.SetOrthographicOrigin(key, Vector3.Zero);
                Equal(Vector3.Zero, ucs.OrthographicOrigins.get_Item(key), "explicit zero override");
                Check(ucs.RemoveOrthographicOrigin(key), "Override not removed.");
                Check(!ucs.RemoveOrthographicOrigin(key), "Absent override reported as removed.");
            }
            const dictionary = ucs.OrthographicOrigins;
            Throws(NotSupportedException,() => dictionary.Add(UcsOrthographicType.Top, Vector3.Zero));
            Equal(0, ucs.OrthographicOrigins.Count, "Read-only mutation changed the model.");
            for (const invalid of [ -1, 0, 7, 32767 ])
            {
                const type = invalid;
                Throws(ArgumentOutOfRangeException,() => ucs.SetOrthographicOrigin(type, Vector3.Zero));
                Throws(ArgumentOutOfRangeException,() => ucs.TryGetOrthographicOrigin(type, {}));
                Throws(ArgumentOutOfRangeException,() => ucs.RemoveOrthographicOrigin(type));
            }
            ucs.SetOrthographicOrigin(UcsOrthographicType.Top, OrthoPoint(1));
            for (const invalid of [ NaN, Infinity, -Infinity ])
                for (const point of [ new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) ])
                {
                    Throws(ArgumentOutOfRangeException,() => ucs.SetOrthographicOrigin(UcsOrthographicType.Top, point));
                    Equal(OrthoPoint(1), ucs.OrthographicOrigins.get_Item(UcsOrthographicType.Top), "Invalid assignment changed stored origin.");
                }
        }
        const original = new UCS("Original",new Vector3(3,4,5),Vector3.UnitY,Vector3.Negate(Vector3.UnitX)); original.Elevation=-2.5;
        for (let type = 1; type <= 6; ++type) original.SetOrthographicOrigin(type, OrthoPoint(type));
        const copy = original.Clone("Copy");
        Equal(6, copy.OrthographicOrigins.Count, "clone override count");
        Equal(original.Origin, copy.Origin, "clone base origin");
        Equal(original.GetTransformation(), copy.GetTransformation(), "clone axes");
        Near(original.Elevation, copy.Elevation, "clone elevation");
        for (const pair of original.OrthographicOrigins) Equal(pair.Value, copy.OrthographicOrigins.get_Item(pair.Key), "clone override");
        copy.RemoveOrthographicOrigin(UcsOrthographicType.Left);
        copy.SetOrthographicOrigin(UcsOrthographicType.Right, Vector3.Zero);
        Equal(6, original.OrthographicOrigins.Count, "clone removal affected source");
        Equal(OrthoPoint(6), original.OrthographicOrigins.get_Item(UcsOrthographicType.Right), "clone edit affected source");
    }


const OrthoPoint = type => new Vector3(type * 1.25, -type * 2.5, type * 3.75);
