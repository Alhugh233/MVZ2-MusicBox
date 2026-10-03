#nullable enable

using System;
using UnityEngine;

namespace MusicBox;

public class MusicBoxBehaviour : MonoBehaviour
{
    public MusicBoxBehaviour(IntPtr ptr) : base(ptr)
    {
    }

    private bool started;

    private void Update()
    {
        try
        {
            if (!started)
            {
                started = true;
                MusicPackLoader.Initialize();
            }
            MusicPackLoader.Update();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MusicBox] Update failed: {e}");
        }
    }
}
