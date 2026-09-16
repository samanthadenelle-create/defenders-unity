// =============================================================================
// OwnedTownSceneBuilder — OwnedTown_IronBastion.unity IS DERIVED, NOT HAND-AUTHORED.
//
// This file is the evidence for that, and WO-1767 made it the ruling. It opens
// RaidBase_IronBastion.unity, deletes the one RaidGarrisonSpawner, flips every DefenseTower to
// PlayerOwned, appends an OwnedTownController root (AFTER every template root, so inherited
// sibling addresses stay stable) and Save-As's the result.
//
// RaidBaseGenerator carried the opposite claim in a comment - "OWNER-AUTHORED AND PROTECTED, NOT
// regenerated" - and believing it is how the pair desynchronised: WO-1732 regenerated the raid
// scene onto the WO-1723 4.0 m partition (210 walls -> 158 + 158 ruins) and left this scene on
// the pre-WO-1723 210-wall one, with all 221 hand-stamped OwnedTemplateIdentity components now
// describing a building that no longer existed. Owner ruling 2026-09-16, verbatim:
//   "(A) re-derive OwnedTown_IronBastion and its manifest from the new 158-wall raid scene"
//
// ⛔ DO NOT CALL THIS ALONE, and never hand-edit either scene (CLAUDE.md §3). The sanctioned
// chain is DeNelle.Editor.OwnedTownChain.RebuildFromRaid, which regenerates the raid template,
// runs this derive, re-verifies the baked stable identities, re-bakes the shipped manifest,
// reseats the spire and bakes navigation - judged by OWNED_TOWN_CHAIN_OK on a fresh log.
//
// Marker: OWNED_TOWN_SCENE_OK. There is deliberately NO census count in this file: the counts
// live in the artifacts and are derived by OwnedTemplateIdentityBake / OwnedTownManifestBake /
// OwnedTownTemplateIdentityRegression (CLAUDE.md §8 - a literal census is the bug).
// =============================================================================
using System;
using System.Collections.Generic;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class OwnedTownSceneBuilder
    {
        public static void Build()
        {
            const string source = "Assets/Scenes/RaidBase_IronBastion.unity";
            string destination = "Assets/Scenes/" + OwnedTownScenePose.SceneName + ".unity";
            var scene = EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
            int spawners = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var spawner in root.GetComponentsInChildren<RaidGarrisonSpawner>(true))
                { UnityEngine.Object.DestroyImmediate(spawner); spawners++; }
                foreach (var tower in root.GetComponentsInChildren<DefenseTower>(true))
                    tower.Allegiance = TowerAllegiance.PlayerOwned;
            }
            if (spawners != 1) throw new InvalidOperationException("Expected exactly one final raid garrison producer.");
            // Append the controller after every template root: inherited sibling addresses stay stable.
            var owner = new GameObject("OwnedTownController");
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.AddComponent<OwnedTownController>();
            if (!EditorSceneManager.SaveScene(scene, destination))
                throw new InvalidOperationException("Owned-town scene could not be saved.");
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == destination);
            if (index >= 0) scenes[index] = new EditorBuildSettingsScene(destination, true);
            else scenes.Add(new EditorBuildSettingsScene(destination, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("OWNED_TOWN_SCENE_OK: separate template saved, garrison removed, towers friendly; source raid not saved");
        }
    }
}
