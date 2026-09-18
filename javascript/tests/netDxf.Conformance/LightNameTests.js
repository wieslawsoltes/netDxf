// Complete detached case from the identically named pinned C# source.
// Typed transport/persistence cases remain unregistered.
import { Light } from '../../index.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
import { Run, Equal, Throws } from './TestHarness.js';
export function RegisterLightNameTests() {
  Run('light/name/nonmutating-validation', () => {
    const light = new Light(); light.Name = 'original';
    Throws(ArgumentNullException, () => { light.Name = null; });
    for (const name of ['\r','\n','\0','A\rB','A\nB','A\0B','\r\n']) {
      Throws(ArgumentException, () => { light.Name = name; });
      Equal('original', light.Name, 'Rejected name mutated state');
    }
  });
}
