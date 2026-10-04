// Observation only: all helper behavior comes from production StringEnum.
import * as enums from '../Enums.generated.js';
import { StringEnum, StringValueAttribute } from '../netDxf/StringEnum.js';
export function stringEnumCall(input) {
  const type = enums[input.enum], Helper = StringEnum.For(type), model = new Helper();
  return input.steps.map(step => {
    try {
      let value;
      switch (step.method) {
        case 'EnumType': value = model.EnumType === type; break;
        case 'Attribute': value = new StringValueAttribute(step.value).Value; break;
        case 'GetStringValues': value = Array.from(model.GetStringValues()); break;
        case 'GetValues': value = Array.from(model.GetValues(), p=>({key:p.Key, value:p.Value})); break;
        default: value = Object.hasOwn(step,'comparison') ? Helper[step.method](step.value, step.comparison) : Helper[step.method](step.value);
      }
      return {ok:true, value};
    } catch (e) { return {ok:false, error:e.name, param:e.ParamName ?? null}; }
  });
}
