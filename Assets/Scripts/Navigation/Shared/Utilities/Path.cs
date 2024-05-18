namespace Navigation.Utilities
{
    public sealed class Path
    {
        public const string AgentsAssetPath = "Assets/Scripts/Navigation/Shared/ScriptableObjects/Agents.asset";
        public const string AreasAssetPath = "Assets/Scripts/Navigation/Shared/ScriptableObjects/Areas.asset";
        public const string BuildInfoAssetPath = "Assets/Scripts/Navigation/Shared/ScriptableObjects/BuildInfo.asset";
        public const string DefaultBuildInfoAssetPath = "Assets/Scripts/Navigation/Shared/ScriptableObjects/BuildInfo_Default.asset";
        public const string AreaMaskFilePath = "Assets/Scripts/Navigation/Shared/Flags/AreaMask.cs";
        public const string AgentTypeFilePath = "Assets/Scripts/Navigation/Shared/Flags/AgentType.cs";

#if UNITY_EDITOR
        public const string SettingDataAssetPath = "Assets/Scripts/Navigation/Editor/Generator/SettingData/SettingData.asset";
#endif
    }
}