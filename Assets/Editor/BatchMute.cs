using UnityEditor;
using UnityEngine;

/// <summary>
/// Test and render runs from the command line (batch mode) make no sound: the owner works on this Mac while they run.
/// Only the master volume is turned down, and only in batch mode, so nothing about the game's own audio logic changes
/// (sources still play, pause still works) and the editor and the phone build sound as before.
/// </summary>
[InitializeOnLoad]
static class BatchMute
{
    static BatchMute()
    {
        if (!Application.isBatchMode) return;
        Mute();
        EditorApplication.playModeStateChanged += _ => Mute();
        EditorApplication.update += Mute;
    }

    static void Mute()
    {
        if (AudioListener.volume != 0f) AudioListener.volume = 0f;
    }
}
