// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class GVector {
  // C# backing state is prefixed with $; public members retain their original names.
  $vector = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching GVector constructor. Use GVector.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(size) {
    this.$vector = GteArray(size, () => 0);
  }
  $ctor1(size, d) {
    this.$vector = GteArray(size, () => 0);
    GteReference(this).MakeUnit(d);
  }
  $ctor2(elements) {
    this.$vector = GteArray(GteReference(elements).length, () => 0);
    GteCopyTo(elements, this.$vector, 0);
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int") {
      if (!(args.length === 1 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GVector.$create0(...args);
    }
    if (signature === "int,int") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GVector.$create1(...args);
    }
    if (signature === "double[]") {
      if (!(args.length === 1 && (args[0] === null || Array.isArray(args[0]) || args[0] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GVector.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new GVector(ConstructorTag, 0, args); }
  static $create1(...args) { return new GVector(ConstructorTag, 1, args); }
  static $create2(...args) { return new GVector(ConstructorTag, 2, args); }
  get Size() {
    return GteReference(this.$vector).length;
  }
  get_Item(i) {
    return GetElement(this.$vector, i);
  }
  set_Item(i, value) {
    SetElement(this.$vector, i, value);
  }
  get Vector() {
    return this.$vector;
  }
  static op_Equality(vec1, vec2) {
    if ((GVector.op_Equality(vec1, null) || GVector.op_Equality(vec2, null)))
    {
      return false;
    }
    return GteReference(vec1).$Equals0(vec2);
  }
  static op_Inequality(vec1, vec2) {
    if ((GVector.op_Equality(vec1, null) || GVector.op_Equality(vec2, null)))
    {
      return false;
    }
    return (!GteReference(vec1).$Equals0(vec2));
  }
  static op_LessThan(vec1, vec2) {
    if ((GteReference(vec1).Size !== GteReference(vec2).Size))
    {
      return false;
    }
    let size = GteReference(vec1).Size;
    for (let i = 0; (i < size); (i++))
    {
      if ((vec1.get_Item(i) < vec2.get_Item(i)))
      {
        return false;
      }
    }
    return true;
  }
  static op_LessThanOrEqual(vec1, vec2) {
    if ((GteReference(vec1).Size !== GteReference(vec2).Size))
    {
      return false;
    }
    let size = GteReference(vec1).Size;
    for (let i = 0; (i <= size); (i++))
    {
      if ((vec1.get_Item(i) < vec2.get_Item(i)))
      {
        return false;
      }
    }
    return true;
  }
  static op_GreaterThan(vec1, vec2) {
    if ((GteReference(vec1).Size !== GteReference(vec2).Size))
    {
      return false;
    }
    let size = GteReference(vec1).Size;
    for (let i = 0; (i < size); (i++))
    {
      if ((vec1.get_Item(i) > vec2.get_Item(i)))
      {
        return false;
      }
    }
    return true;
  }
  static op_GreaterThanOrEqual(vec1, vec2) {
    if ((GteReference(vec1).Size !== GteReference(vec2).Size))
    {
      return false;
    }
    let size = GteReference(vec1).Size;
    for (let i = 0; (i < size); (i++))
    {
      if ((vec1.get_Item(i) >= vec2.get_Item(i)))
      {
        return false;
      }
    }
    return true;
  }
  MakeZero() {
    for (let i = 0; (i < GteReference(this.$vector).length); (i++))
    {
      SetElement(this.$vector, i, 0);
    }
  }
  MakeUnit(d) {
    for (let i = 0; (i < GteReference(this.$vector).length); (i++))
    {
      if ((i === d))
      {
        SetElement(this.$vector, i, 1);
      }
      else
      {
        SetElement(this.$vector, i, 0);
      }
    }
  }
  static Zero(size) {
    return GVector.$create0(size);
  }
  static Unit(size, d) {
    return GVector.$create1(size, d);
  }
  static op_Addition(vec1, vec2) {
    if ((GVector.op_Equality(vec1, null) || GVector.op_Equality(vec2, null)))
    {
      return null;
    }
    if ((GteReference(vec1).Size === GteReference(vec2).Size))
    {
      let size = GteReference(vec1).Size;
      let vector = GVector.$create0(size);
      for (let i = 0; (i < size); (i++))
      {
        vector.set_Item(i, (vec1.get_Item(i) + vec2.get_Item(i)));
      }
      return vector;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return null;
  }
  static op_Subtraction(vec1, vec2) {
    if ((GVector.op_Equality(vec1, null) || GVector.op_Equality(vec2, null)))
    {
      return null;
    }
    if ((GteReference(vec1).Size === GteReference(vec2).Size))
    {
      let size = GteReference(vec1).Size;
      let vector = GVector.$create0(size);
      for (let i = 0; (i < size); (i++))
      {
        vector.set_Item(i, (vec1.get_Item(i) - vec2.get_Item(i)));
      }
      return vector;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return null;
  }
  static $op_Multiply0(scalar, vec) {
    if (GVector.op_Equality(vec, null))
    {
      return null;
    }
    let size = GteReference(vec).Size;
    let vector = GVector.$create0(size);
    for (let i = 0; (i < size); (i++))
    {
      vector.set_Item(i, MultiplyDouble(scalar, vec.get_Item(i)));
    }
    return vector;
  }
  static $op_Multiply1(vec, scalar) {
    return GVector.$op_Multiply0(scalar, vec);
  }
  static op_Division(vec, scalar) {
    return GVector.$op_Multiply1(vec, ((1 / scalar)));
  }
  static Dot(v0, v1) {
    if ((GVector.op_Equality(v0, null) || GVector.op_Equality(v1, null)))
    {
      return DotNetNaN;
    }
    if ((GteReference(v0).Size === GteReference(v1).Size))
    {
      let dot = 0;
      for (let i = 0; (i < GteReference(v0).Size); (i++))
      {
        dot += MultiplyDouble(v0.get_Item(i), v1.get_Item(i));
      }
      return dot;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return DotNetNaN;
  }
  static Length(v, robust = false) {
    if (GVector.op_Equality(v, null))
    {
      return DotNetNaN;
    }
    if (robust)
    {
      let maxAbsComp = DotNetMath.Abs(v.get_Item(0));
      for (let i = 1; (i < GteReference(v).Size); (++i))
      {
        let absComp = DotNetMath.Abs(v.get_Item(i));
        if ((absComp > maxAbsComp))
        {
          maxAbsComp = absComp;
        }
      }
      let length = 0;
      if ((maxAbsComp > 0))
      {
        let scaled = GVector.op_Division(v, maxAbsComp);
        length = MultiplyDouble(maxAbsComp, DotNetMath.Sqrt(GVector.Dot(scaled, scaled)));
      }
      else
      {
        length = 0;
      }
      return length;
    }
    return DotNetMath.Sqrt(GVector.Dot(v, v));
  }
  static Normalize(v, robust = false) {
    if (GVector.op_Equality(v.value, null))
    {
      return DotNetNaN;
    }
    if (robust)
    {
      let maxAbsComp = DotNetMath.Abs(v.value.get_Item(0));
      for (let i = 1; (i < GteReference(v.value).Size); (i++))
      {
        let absComp = DotNetMath.Abs(v.value.get_Item(i));
        if ((absComp > maxAbsComp))
        {
          maxAbsComp = absComp;
        }
      }
      let length = 0;
      if ((maxAbsComp > 0))
      {
        v.value = GVector.op_Division(v.value, maxAbsComp);
        length = DotNetMath.Abs(GVector.Dot(v.value, v.value));
        v.value = GVector.op_Division(v.value, length);
        length *= maxAbsComp;
      }
      else
      {
        length = 0;
        for (let i = 0; (i < GteReference(v.value).Size); (i++))
        {
          v.value.set_Item(i, 0);
        }
      }
      return length;
    }
    else
    {
      let length = DotNetMath.Sqrt(GVector.Dot(v.value, v.value));
      if ((length > 0))
      {
        v.value = GVector.op_Division(v.value, length);
      }
      else
      {
        for (let i = 0; (i < GteReference(v.value).Size); (i++))
        {
          v.value.set_Item(i, 0);
        }
      }
      return length;
    }
  }
  static Orthonormalize(numInputs, v, robust = false) {
    if ((((v.value !== null) && (1 <= numInputs)) && (numInputs <= GteReference(GetElement(v.value, 0)).Size)))
    {
      for (let i = 1; (i < numInputs); (++i))
      {
        if ((GteReference(GetElement(v.value, 0)).Size !== GteReference(GetElement(v.value, i)).Size))
        {
          // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
        }
      }
      let minLength = GVector.Normalize(GteElementRef(v.value, [0], false), robust);
      for (let i = 1; (i < numInputs); (++i))
      {
        for (let j = 0; (j < i); (++j))
        {
          let dot = GVector.Dot(GetElement(v.value, i), GetElement(v.value, j));
          SetElement(v.value, i, GVector.op_Subtraction(GetElement(v.value, i), GVector.$op_Multiply1(GetElement(v.value, j), dot)));
        }
        let length = GVector.Normalize(GteElementRef(v.value, [i], false), robust);
        if ((length < minLength))
        {
          minLength = length;
        }
      }
      return minLength;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return DotNetNaN;
  }
  static ComputeExtremes(numVectors, v, vmin, vmax) {
    if (((v !== null) && (numVectors > 0)))
    {
      for (let i = 1; (i < numVectors); (++i))
      {
        if ((GteReference(GetElement(v, 0)).Size !== GteReference(GetElement(v, i)).Size))
        {
          // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
        }
      }
      let size = GteReference(GetElement(v, 0)).Size;
      vmin.value = GetElement(v, 0);
      vmax.value = vmin.value;
      for (let j = 1; (j < numVectors); (++j))
      {
        let vec = GetElement(v, j);
        for (let i = 0; (i < size); (++i))
        {
          if ((vec.get_Item(i) < vmin.value.get_Item(i)))
          {
            vmin.value.set_Item(i, vec.get_Item(i));
          }
          else
          if ((vec.get_Item(i) > vmax.value.get_Item(i)))
          {
            vmax.value.set_Item(i, vec.get_Item(i));
          }
        }
      }
      return true;
    }
    vmin.value = null;
    vmax.value = null;
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return false;
  }
  HLift(v, last) {
    if (GVector.op_Equality(v, null))
    {
      return null;
    }
    let size = GteReference(v).Size;
    let result = GVector.$create0((size + 1));
    for (let i = 0; (i < size); (i++))
    {
      result.set_Item(i, v.get_Item(i));
    }
    result.set_Item(size, last);
    return result;
  }
  HProject(v) {
    if (GVector.op_Equality(v, null))
    {
      return null;
    }
    let size = GteReference(v).Size;
    if ((size > 1))
    {
      let result = GVector.$create0((size - 1));
      for (let i = 0; (i < (size - 1)); (i++))
      {
        result.set_Item(i, v.get_Item(i));
      }
      return result;
    }
    return null;
  }
  Lift(v, inject, value) {
    let size = GteReference(v).Size;
    let result = GVector.$create0((size + 1));
    let i = 0;
    for (i = 0; (i < inject); (++i))
    {
      result.set_Item(i, v.get_Item(i));
    }
    result.set_Item(i, value);
    let j = i;
    for ((++j); (i < size); (++i), (++j))
    {
      result.set_Item(j, v.get_Item(i));
    }
    return result;
  }
  Project(v, reject) {
    let size = GteReference(v).Size;
    if ((size > 1))
    {
      let result = GVector.$create0((size - 1));
      for (let i = 0, j = 0; (i < (size - 1)); (++i), (++j))
      {
        if ((j === reject))
        {
          (++j);
        }
        result.set_Item(i, v.get_Item(j));
      }
      return result;
    }
    return GVector.$create0(0);
  }
  $Equals0(other) {
    if (GVector.op_Equality(other, null))
    {
      return false;
    }
    if ((GteReference(this).Size !== GteReference(other).Size))
    {
      return false;
    }
    let size = GteReference(this).Size;
    for (let i = 0; (i < size); (i++))
    {
      if ((DotNetMath.Abs((this.get_Item(i) - other.get_Item(i))) > 5E-324))
      {
        return false;
      }
    }
    return true;
  }
  $Equals1(obj) {
    if ((obj === null))
    {
      return false;
    }
    return ((GteReference(obj).constructor === GteReference(this).constructor) && GteReference(this).$Equals0(obj));
  }
  GetHashCode() {
    return GteHash(this.$vector);
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] === null || args[1] instanceof GVector)) return GVector.$op_Multiply0(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof GVector) && (typeof args[1] === 'number')) return GVector.$op_Multiply1(...args);
    throw new ArgumentException("No matching GVector.op_Multiply overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 1 && (args[0] === null || args[0] instanceof GVector)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals1(...args);
    throw new ArgumentException("No matching GVector.Equals overload. Consult native-port-manifest.json.");
  }
}
