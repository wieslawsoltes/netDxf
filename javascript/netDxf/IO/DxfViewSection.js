// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Section } from '../Entities/Section.js';
import { WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidOperationException, NotSupportedException, InvalidDataException } from '../../runtime/Errors.js';
export function ResolveViewSections(context) {
  for (const [view, handle] of context.loadedViewSections) {
    if (context.GetObjectBySourceHandle(view.Handle) !== view)
      throw new InvalidDataException('A VIEW live-section referrer must retain its exact physical source identity.');
    let section = null;
    if (handle !== '0') {
      const target = context.GetObjectBySourceHandle(handle); section = target instanceof Section ? target : null;
      if (section === null) throw new InvalidDataException('VIEW group 334 must resolve to an actual source SECTION or SECTIONOBJECT.');
    }
    try { view.LiveSection = section; }
    catch (error) {
      if (error instanceof ArgumentException || error instanceof InvalidOperationException || error instanceof NotSupportedException)
        throw WrappedInvalidData('Invalid VIEW live-section relationship.', error);
      throw error;
    }
  }
}
