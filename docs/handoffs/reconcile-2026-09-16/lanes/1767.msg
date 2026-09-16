fix(capture): WO-1767 - Bastion capture census: deterministic template ids from the generator, owned town + practice arena re-derived from the 158-wall raid scene through one chain

Captured on the owner's Seeker (build 371701, 13:24:27): "Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_0". WO-1732's regeneration (452fc14fd) had dropped all 221 OwnedTemplateIdentity stamps and moved the raid scene to 158 walls while OwnedTown_IronBastion, its manifest and ArenaPractice_IronBastion still described the old 210-wall town - a 3-star clear would strand the player on the victory screen.

Owner ruling 2026-09-16 (verbatim choice): "Re-derive the town from the new raid scene". RaidBaseGenerator now emits _templateId at creation as <configId>.<role>.<index> (no Guid); the bakes derive every count from the artifact (no 221 literal); OwnedTownChain.RebuildFromRaid runs BuildSceneFor -> stamp -> OwnedTownSceneBuilder -> manifest -> reseat -> RaidNavBake -> practice scene, withholding OWNED_TOWN_CHAIN_OK on any missing step marker. Result: raid 169 / town 169 / practice 169 / manifest 169, id sets equal, ids identical across three independent bakes. OwnedTownTemplateIdentityRegression lints all three scenes + manifest and went red on the real defect before the chain, green after. The six RaidGround keep .mat files are the nav bake's own re-derivation.

Gated: COMPILE_GATE_OK (2026-09-16 15:15) + REGRESSION_OK 550/550 suites (2026-09-16 15:31); OWNED_TOWN_CHAIN_OK (Builds/owned-town-chain3.log, copied to logs/debug/wo1767-chain-run4-practice.log).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
