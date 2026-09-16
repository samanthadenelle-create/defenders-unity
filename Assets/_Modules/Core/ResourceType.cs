// =============================================================================
// ResourceType — the harvestable resource kinds, Core-visible (WO-117).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core
//
// Lives in Core (pure data) so Village MonoBehaviours AND future HUD readouts can
// both name a resource without an assembly reference to Village (CLAUDE.md §5).
//
// RECONCILIATION (WO-117): the in-world node already exists as
// DeNelle.Village.MineNode with its own DeNelle.Village.MineResource enum. We do
// NOT introduce a parallel node type. This Core enum is the cross-assembly mirror
// of MineResource and maps 1:1 to the SAME existing GameState wallet fields:
//
//   ResourceType.Iron          → GameState.Iron
//   ResourceType.Wood          → GameState.Wood
//   ResourceType.Stone          → GameState.Resources.Stone   (DEF-121: retired "Stone" axis)
//   ResourceType.AetherCrystal → GameState.AetherCrystals
//
// Banking itself stays in MineNode (the single source of truth); this enum only
// exists so HUD / save / offline-accrual code can talk about a resource kind in
// Core terms. No new currency is added — all four fields already exist.
// =============================================================================

namespace DeNelle.Core
{
    /// <summary>Cross-assembly mirror of DeNelle.Village.MineResource. Maps 1:1 to an
    /// existing GameState wallet field — no net-new currency (WO-117 §1).</summary>
    public enum ResourceType
    {
        Iron = 0,
        Wood = 1,
        [System.Runtime.Serialization.EnumMember(Value = "Food")]
        Stone = 2,
        AetherCrystal = 3,
    }
}
