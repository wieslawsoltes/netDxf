import { lightListCorpus } from './light-corpus.mjs';
// Keep all existing scenarios intact and compose new typed-model coverage.
import { databaseModelCorpus as coreCorpus } from './database-model-core-corpus.mjs';
import { indexFilterCorpus } from './index-filter-corpus.mjs';
export function databaseModelCorpus() { return coreCorpus().concat(indexFilterCorpus(),lightListCorpus()); }
