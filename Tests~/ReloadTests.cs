using UnityEditor;
using UnityEngine;

public static class ReloadTests
{
    [MenuItem("Tests/Trigger 5 Rapid Reloads")]
    public static void TriggerRapidReloads()
    {
        EditorApplication.delayCall += () => TriggerReloadSequence(5);
    }

    private static void TriggerReloadSequence(int remaining)
    {
        if (remaining <= 0) return;

        Debug.Log($"Triggering reload {remaining}...");
        EditorUtility.RequestScriptReload();

        EditorApplication.delayCall += () =>
        {
            // Wait a bit, then trigger next
            EditorApplication.delayCall += () => TriggerReloadSequence(remaining - 1);
        };
    }
}
