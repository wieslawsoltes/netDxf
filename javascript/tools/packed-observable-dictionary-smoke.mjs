// Appended to the offline-installed package smoke suite. No development imports.
{
  const { ObservableDictionary, ObservableDictionaryEventArgs, BoxedString, Vector2 } = await import('@netdxf/javascript');
  const { ObservableDictionary: Standalone } = await import('@netdxf/javascript/netDxf/Collections/ObservableDictionary.js');
  if (Standalone !== ObservableDictionary) throw new Error('Dictionary standalone export differs.');
  const dictionary = new ObservableDictionary(0, null, 'string', 'string');
  const shared = new BoxedString('value'), log = [];
  dictionary.Add('key', shared);
  dictionary.AddItem.Add((_, e) => log.push(e.Item.Value));
  dictionary.set_Item('key', shared);
  if (log.length !== 1 || dictionary.RemovePair({ Key: 'key', Value: new BoxedString('value') }) ||
      !dictionary.RemovePair({ Key: 'key', Value: shared })) throw new Error('Packed dictionary identity/event mismatch.');
  const vectors = new ObservableDictionary(0, null, 'string', Vector2), out = {};
  vectors.TryGetValue('missing', out);
  if (out.value.X !== 0 || out.value.Y !== 0) throw new Error('Packed generic default mismatch.');
  const input = new Vector2(1, 2), event = new ObservableDictionaryEventArgs({ Key: 'key', Value: input });
  input.X = 9; event.Item.Value.Y = 8;
  if (event.Item.Value.X !== 1 || event.Item.Value.Y !== 2) throw new Error('Packed event arguments lost value copies.');
}
