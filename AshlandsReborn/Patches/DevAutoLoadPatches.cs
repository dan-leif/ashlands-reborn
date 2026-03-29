using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Automatically navigates FejdStartup menus (Main Menu -> Character Select -> World Select -> Start)
/// when Plugin.DevAutoLoad is enabled. Called from Plugin.Update() each frame.
/// </summary>
internal static class DevAutoLoadPatches
{
    private enum AutoLoadState
    {
        WaitForMainMenu,
        WaitForCharacterScreen,
        WaitForWorldScreen,
        Done
    }

    private static AutoLoadState _state = AutoLoadState.WaitForMainMenu;
    private static bool _started;

    // Cached reflection fields (resolved once)
    private static FieldInfo? _profilesField;
    private static FieldInfo? _profileIndexField;
    private static FieldInfo? _worldsField;
    private static FieldInfo? _worldField;
    private static MethodInfo? _onStartGameMethod;
    private static MethodInfo? _onCharacterStartMethod;
    private static MethodInfo? _onWorldStartMethod;
    private static bool _reflectionResolved;

    /// <summary>
    /// Called from Plugin.Update() every frame. Drives the auto-load state machine.
    /// Returns immediately if DevAutoLoad is disabled or already finished.
    /// </summary>
    private static float _lastDebugLogTime;

    internal static void Tick()
    {
        // Periodic debug log (once per 5s) to confirm Tick is running
        if (!_started && Time.time - _lastDebugLogTime > 5f)
        {
            _lastDebugLogTime = Time.time;
            var cfgVal = Plugin.DevAutoLoad?.Value;
            var inst = FejdStartup.instance;
            Plugin.Log.LogInfo($"[DevAutoLoad] Tick debug: config={cfgVal}, instance={inst != null}, state={_state}");
        }

        if (!(Plugin.DevAutoLoad?.Value ?? false))
            return;

        if (_state == AutoLoadState.Done)
            return;

        var instance = FejdStartup.instance;
        if (instance == null)
            return;

        if (!_started)
        {
            _started = true;
            Plugin.Log.LogInfo("[DevAutoLoad] Enabled - will auto-navigate menus");
        }

        if (!ResolveReflection())
            return;

        switch (_state)
        {
            case AutoLoadState.WaitForMainMenu:
                HandleWaitForMainMenu(instance);
                break;
            case AutoLoadState.WaitForCharacterScreen:
                HandleWaitForCharacterScreen(instance);
                break;
            case AutoLoadState.WaitForWorldScreen:
                HandleWaitForWorldScreen(instance);
                break;
        }
    }

    private static void HandleWaitForMainMenu(FejdStartup instance)
    {
        if (instance.m_mainMenu == null || !instance.m_mainMenu.activeInHierarchy)
            return;

        Plugin.Log.LogInfo("[DevAutoLoad] Main menu ready - clicking Start Game");
        _onStartGameMethod!.Invoke(instance, null);
        _state = AutoLoadState.WaitForCharacterScreen;
    }

    private static void HandleWaitForCharacterScreen(FejdStartup instance)
    {
        if (instance.m_characterSelectScreen == null || !instance.m_characterSelectScreen.activeInHierarchy)
            return;

        var profiles = _profilesField!.GetValue(instance) as System.Collections.IList;
        if (profiles == null || profiles.Count == 0)
            return;

        var targetName = Plugin.DevAutoLoadCharacter?.Value ?? "Dove";
        int foundIndex = -1;

        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var getName = profile.GetType().GetMethod("GetName", BindingFlags.Public | BindingFlags.Instance);
            if (getName == null) continue;

            var name = getName.Invoke(profile, null) as string;
            if (name != null && name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0)
        {
            Plugin.Log.LogWarning($"[DevAutoLoad] Character '{targetName}' not found in {profiles.Count} profiles - aborting");
            _state = AutoLoadState.Done;
            return;
        }

        Plugin.Log.LogInfo($"[DevAutoLoad] Selecting character '{targetName}' (index {foundIndex})");
        _profileIndexField!.SetValue(instance, foundIndex);
        _onCharacterStartMethod!.Invoke(instance, null);
        _state = AutoLoadState.WaitForWorldScreen;
    }

    private static void HandleWaitForWorldScreen(FejdStartup instance)
    {
        if (instance.m_startGamePanel == null || !instance.m_startGamePanel.activeInHierarchy)
            return;

        var worlds = _worldsField!.GetValue(instance) as System.Collections.IList;
        if (worlds == null || worlds.Count == 0)
            return;

        var targetName = Plugin.DevAutoLoadWorld?.Value ?? "Reborn";
        object? foundWorld = null;

        for (int i = 0; i < worlds.Count; i++)
        {
            var world = worlds[i];
            var nameField = world.GetType().GetField("m_name", BindingFlags.Public | BindingFlags.Instance);
            if (nameField == null) continue;

            var name = nameField.GetValue(world) as string;
            if (name != null && name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                foundWorld = world;
                break;
            }
        }

        if (foundWorld == null)
        {
            Plugin.Log.LogWarning($"[DevAutoLoad] World '{targetName}' not found in {worlds.Count} worlds - aborting");
            _state = AutoLoadState.Done;
            return;
        }

        Plugin.Log.LogInfo($"[DevAutoLoad] Selecting world '{targetName}' - starting game");
        _worldField!.SetValue(instance, foundWorld);
        _onWorldStartMethod!.Invoke(instance, null);
        _state = AutoLoadState.Done;
    }

    private static bool ResolveReflection()
    {
        if (_reflectionResolved)
            return true;

        var type = typeof(FejdStartup);

        _profilesField = AccessTools.Field(type, "m_profiles");
        _profileIndexField = AccessTools.Field(type, "m_profileIndex");
        _worldsField = AccessTools.Field(type, "m_worlds");
        _worldField = AccessTools.Field(type, "m_world");

        _onStartGameMethod = AccessTools.Method(type, "OnStartGame");
        _onCharacterStartMethod = AccessTools.Method(type, "OnCharacterStart");
        _onWorldStartMethod = AccessTools.Method(type, "OnWorldStart");

        if (_profilesField == null || _profileIndexField == null ||
            _worldsField == null || _worldField == null ||
            _onStartGameMethod == null || _onCharacterStartMethod == null ||
            _onWorldStartMethod == null)
        {
            Plugin.Log.LogError("[DevAutoLoad] Failed to resolve FejdStartup fields/methods via reflection - aborting");
            _state = AutoLoadState.Done;
            return false;
        }

        _reflectionResolved = true;
        return true;
    }
}
