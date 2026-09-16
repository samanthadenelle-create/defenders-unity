using System;
using System.Collections.Generic;
using DeNelle.Core.Combat;
using DeNelle.Village.Arena;
using DeNelle.Village.World.Camps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownPracticeSceneBuilder
    {
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/OwnedTown_IronBastion.unity", OpenSceneMode.Single);
            int controllers = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var owner in root.GetComponentsInChildren<OwnedTownController>(true))
                {
                    var host = owner.gameObject;
                    UnityEngine.Object.DestroyImmediate(owner);
                    host.AddComponent<OwnedTownPracticeController>(); controllers++;
                }
            if (controllers != 1) throw new InvalidOperationException("Expected one owned template controller.");
            string path = "Assets/Scenes/" + PracticeCombatPolicy.SceneName + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Practice scene save failed.");
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int found = scenes.FindIndex(s => s.path == path);
            if (found >= 0) scenes[found] = new EditorBuildSettingsScene(path, true);
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("OWNED_TOWN_PRACTICE_SCENE_OK isolated template copy; original town and raid not saved");
        }
    }
}
