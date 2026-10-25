using UnityEditor;

namespace SwarmPrototype
{
    // Scripts in this project are often edited while the game is running. Unity's
    // default is to recompile and keep playing, which reloads the C# domain and
    // wipes statics and non-serialized fields on live objects (NavMesh paths,
    // animation graphs, GameState), so the match breaks in confusing ways.
    // This makes the editor wait until Play is stopped before recompiling.
    // Same as Preferences > General > Script Changes While Playing.
    [InitializeOnLoad]
    static class EditorGuards
    {
        const string Key = "ScriptCompilationDuringPlay";
        const int RecompileAfterFinishedPlaying = 1;

        static EditorGuards()
        {
            if (EditorPrefs.GetInt(Key, 0) != RecompileAfterFinishedPlaying)
                EditorPrefs.SetInt(Key, RecompileAfterFinishedPlaying);
        }
    }
}
