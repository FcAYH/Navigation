using Navigation.Utilities;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// Generator Window的一些设置项
/// 包括 Logger 配置信息，数据输出设置等
/// </summary>

[CreateAssetMenu(menuName = "Navigation/Generator/SettingData")]
public class SettingData : ScriptableObject
{
    #region Logger 设置
    [ReadOnly]
    public LogLevel Level = LogLevel.Info;
    [ReadOnly]
    public bool UseUnityDebug = false;
    [ReadOnly]
    public bool ShowFileInfo = true;
    [ReadOnly]
    public bool ShowLevel = true;
    [ReadOnly]
    public bool ShowDateTime = true;
    #endregion

    #region 数据持久化选项
    [ReadOnly]
    public bool SaveSolidHeightField = true;
    [ReadOnly]
    public bool SaveCompactHeightField = true;
    [ReadOnly]
    public bool SaveContours = true;

    /// <summary>
    /// 必须开启，无法被用户编辑为 false，
    /// 同时即便该值为 false，依然会保存 NavMesh 数据
    /// </summary>
    [ReadOnly]
    public bool SaveNavMeshData = true;
    #endregion
}
