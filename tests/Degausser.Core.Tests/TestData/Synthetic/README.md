# Synthetic BDX/BBP golden pair

These two binary files were created specifically as Degausser test data. They are **not** transcriptions of a commercial/copyrighted song.

`degausser-test-scale.bdx` was built from zero bytes specifically for this test suite and contains a simple ascending C-major scale, synthetic metadata, one tempo change, one volume change, and a deliberately useful nonlinear pan value.

`degausser-test-scale.bbp` originated from the independent 2026 Python reference converter and is retained as a golden regression artefact. It may receive narrowly documented archaeology-approved byte corrections when later evidence supersedes an old oracle default.

The pair exists so production C# can be tested against actual binary files, including a whole-file byte-for-byte BDX → BBP assertion.

Corrections to the oracle's bytes:

- 2026-11-09: `title_simple` (manager item `+0x76`) is lowercase, `degausser test scale`. The oracle copied the title; across the 9,722 official BBPs `title_simple` never starts with an uppercase ASCII letter, and the game files a song whose `title_simple` starts with one under symbols (hardware-observed). See `JbMgr.Item.SimpleTitle`.
