#if UNITY_EDITOR
using System;
using System.IO;

namespace DeNelle.Editor.Regression
{
    /// <summary>Locks class-authored primary attacks and the Knight's held shield pose.</summary>
    public static class ClassPrimaryAndKnightBlockRegression
    {
        public static bool Run(out string reason)
        {
            try
            {
                string bridge = File.ReadAllText("Assets/_Modules/Village/HUD/HudKitCommandBridge.cs");
                // ⛔ RE-POINTED 2026-09-06 WITH THE RULING (WO-1429). These two lines used to
                // Require the per-class table `string.Equals(heroClass, "mage"/"ranger")`. That
                // table WAS THE DEFECT: it caught the two classes by NAME, called TryCast(Q), and
                // `return`ed before ever reaching the melee swing - so a refused cast (cooldown OR
                // cost) produced NO verb at all. Captured on a real Seeker at cd=0.47s with mana
                // 21.08/24.00 (logs/device/freeze-20260904-095249.log:544639).
                // The table is DELETED, not extended. The same decision is now DERIVED from the
                // ability's own shape via HeroAbilities.TryGetRangedPrimary, which independently
                // resolves to exactly the same two classes - which is the proof the table was
                // doing a job the data could already do.
                // A pin that requires the defect is a pin that forbids the fix.
                Forbid(bridge, "string.Equals(heroClass, \"mage\"");
                Forbid(bridge, "string.Equals(heroClass, \"ranger\"");
                Require(bridge, "TryGetRangedPrimary");
                Require(bridge, "abilities.TryCast(AbilitySlot.Q)");
                Require(bridge, "atk.TriggerBasicAttack()");

                string hud = File.ReadAllText("Assets/_Modules/HUD/Kit/HudKitController.cs");
                Require(hud, "var q = a.Slots[0]");
                // ⛔ RE-POINTED 2026-09-10 WITH THE RULING (WO-1695), by the SAME reasoning the
                // WO-1429 block above records — and this one needs saying carefully, because the
                // old token was not itself a defect, it was merely a WRITE SITE.
                //
                // WHAT THIS ROW IS ACTUALLY FOR: that the class-authored Q reaches the combat
                // dock's primary FACE as art. `Require(hud, "primary.SetIcon")` was one particular
                // spelling of that — the literal inline write. It could never distinguish a face
                // that paints the class Q from one that paints the crossed-swords DEFAULT, which
                // is exactly what WO-1695 found the Knight doing on the owner's Seeker
                // (Builds/device-frames/2026-09-10_1520_owner_icons.png, build 363866): the inline
                // `primary.SetIcon(UiStyle.Icon(q.IconKey))` DROPPED the in-band "text:" IconKey
                // mode that AbilityLoadoutProducer forces for knight.q
                // (HudModelProducers.cs:619), resolved nothing, and fell to
                // ConceptIconResolver.DefaultSprite(). **The old pin was GREEN throughout.**
                //
                // The fix routes the face through ONE art rule, HudKitController
                // .ApplyCombatPrimaryFaceArt, so the rail and the dock cannot disagree about what
                // an IconKey means — and the write moved inside it, because the two branches
                // (SetLabel for text, SetIcon for a concept) write DIFFERENT members. Keeping a
                // literal `primary.SetIcon` at the call site would mean putting that branch back
                // at the call site, i.e. re-creating the divergence WO-1695 closed, purely to
                // satisfy a string. That is a pin dictating a shape, so it is re-pointed rather
                // than worked around, and the WO says so.
                //
                // The replacement is STRICTLY STRONGER: it names the call AND that `q.IconKey`
                // (the class Q's own resolved concept) is what is handed to it, so a face wired to
                // a constant, to another slot, or to nothing at all now FAILS where the old token
                // passed. The Forbid re-arms the revert guard the way WO-1429 did: the exact
                // pre-WO-1695 line cannot come back and still pass this suite.
                Require(hud, "ApplyCombatPrimaryFaceArt(primary, q.IconKey)");
                Forbid(hud, "primary.SetIcon(string.IsNullOrEmpty(q.IconKey)");
                Require(hud, "primary.SetCaption");

                string factory = File.ReadAllText("Assets/Editor/HeroAnimatorFactory.cs");
                Require(factory, "blockClipOverride = \"sword and shield block idle\"");
                Require(factory, "AddCondition(AnimatorConditionMode.If, 0f, \"Block\")");
                Require(factory, "AddCondition(AnimatorConditionMode.IfNot, 0f, \"Block\")");

                string skip = File.ReadAllText("Assets/_Modules/Core/UI/TutorialSkipUi.cs");
                Require(skip, "face.raycastTarget = true");
                Require(skip, "faceGo.transform.SetParent(_button.transform, false)");
                Require(skip, "face.transform.IsChildOf(_button.transform)");
                Require(skip, "CanvasSortOrder = 6000");
                Require(skip, "canvas.overrideSorting = true");
                Require(skip, "EventSystem.current.RaycastAll(pointer, hits)");
                Require(skip, "SKIP_TOP_HIT_OK");
                Require(skip, "SKIP_TOP_HIT_BLOCKED");

                string ui = File.ReadAllText("Assets/_Modules/Core/UI/ElarionUiKit.cs");
                Require(ui, "MedallionBounds");
                Require(ui, "AspectRatioFitter.AspectMode.FitInParent");
                Require(ui, "const float iconInset = 0.14f");
                Require(ui, "maskGo.transform.SetParent(medallionBounds");
                Require(ui, "bezelGo.transform.SetParent(medallionBounds");

                reason = "CLASS_PRIMARY_BLOCK_OK: class primaries, Knight shield pose, Skip raycast, and square circular-medallion bounds are locked.";
                return true;
            }
            catch (Exception ex)
            {
                reason = "CLASS_PRIMARY_BLOCK_FAIL: " + ex.Message;
                return false;
            }
        }

        private static void Require(string source, string token)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("missing contract: " + token);
        }

        /// <summary>WO-1429 - the inverse of <see cref="Require"/>: a token that MUST be gone.
        /// Used when a pin re-points from "the old shape exists" to "the old shape is retired",
        /// so a revert cannot pass this suite.</summary>
        private static void Forbid(string src, string token)
        {
            if (src != null && src.Contains(token))
                throw new InvalidOperationException("retired contract is BACK: " + token);
        }
    }
}
#endif
