// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfAtomicFile } from './IO/DxfAtomicFile.js';
import { DxfWriter } from './IO/DxfWriter.js';
import { GetFileSystemAdapter } from '../runtime/FileSystem.js';
import { GetTypedDocumentFileHost } from '../runtime/TypedDocumentIO.js';
import { ArgumentNullException, ThrowIfCancellationRequested } from '../runtime/Errors.js';
/** Source SaveAtomic restores path state on failure, not handles or document mutations.
 * Unlike legacy Save this API propagates serialization exceptions in both profiles. */
export function SaveAtomic(document,file,isBinary=false,cancellationToken=null){
  if(file==null)throw new ArgumentNullException('file');
  ThrowIfCancellationRequested(cancellationToken);
  const destination=GetFileSystemAdapter().GetFullPath(file),host=GetTypedDocumentFileHost();
  const previousName=document.Name,previousFolder=document.SupportFolders.WorkingFolder;
  let committed=false;
  try{
    DxfAtomicFile.Write(destination,stream=>{
      document.Name=host.GetFileNameWithoutExtension(destination);
      document.SupportFolders.WorkingFolder=host.GetDirectoryName(destination);
      new DxfWriter().Write(stream,document,isBinary);
    },cancellationToken);
    committed=true;
  }finally{
    if(!committed){document.Name=previousName;document.SupportFolders.WorkingFolder=previousFolder;}
  }
}
