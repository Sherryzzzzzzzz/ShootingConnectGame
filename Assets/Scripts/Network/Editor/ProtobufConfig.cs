// ============================================================
// ProtobufConfig.cs — Protobuf 生成配置（参考 SpaceBuilder）
//
// 使用方式：
//   1. 在 Unity 中：Assets → Create → ShootingGame → ProtobufConfig
//   2. 配置 protoc.exe 路径
//   3. 配置 .proto 源目录和输出目录
//   4. 菜单 Tools → ShootingGame → Update Protobuf
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShootingGame.Editor
{
    [CreateAssetMenu(menuName = "ShootingGame/ProtobufConfig", fileName = "ProtobufConfig")]
    public class ProtobufConfig : ScriptableObject
    {
        [Header("Proto 配置")]
        [Tooltip("protoc.exe 的完整路径")]
        public string ProtocPath;

        [Tooltip(".proto 源文件目录列表")]
        public List<string> ProtoSourceDirs = new List<string>
        {
            "Server/ShootingGame.Shared/Protocol/Proto"
        };

        [Tooltip("生成的 C# 输出目录列表（与 ProtoSourceDirs 一一对应）")]
        public List<string> GeneratedOutputDirs = new List<string>
        {
            "Server/ShootingGame.Shared/Protocol/Generated"
        };
    }
}
