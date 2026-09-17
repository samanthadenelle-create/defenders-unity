fix(town): the stone stockpile stacks rough stone chunks, not flour sacks

Owner: "do we stack rocks? OR stack bags of flour still?" Food became Stone on 09-11 but CollectorStackPropCatalogBuilder still picked Food_Flour.fbx. Pick -> Stone_Chunks_Small.fbx (rough-stone canon 09-09); the retired row was removed per the builder's own instruction and rebuilt (COLLECTOR_PROP_CATALOG_BUILT, wired Stone -> Stone_Chunks_Small scale=0.227).

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
