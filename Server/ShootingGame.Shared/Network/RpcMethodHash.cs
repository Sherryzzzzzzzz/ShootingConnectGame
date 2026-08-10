using System;
using System.Security.Cryptography;
using System.Text;

namespace ShootingGame.Network
{
    /// <summary>
    /// RPC 方法 Hash 计算（与 RpcMethodGenerator.ComputeMethodHash 同算法）。
    /// 签名格式：global::FullTypeName.MethodName(System.Single,System.Int32,...)
    /// 服务器/客户端/测试三端用同一算法识别 RPC 方法。
    /// </summary>
    public static class RpcMethodHash
    {
        public static long Compute(string signature)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(signature));
            return BitConverter.ToInt64(bytes, 0);
        }

        /// <summary>按生成器规则拼签名：global::Type.Method(ParamTypes)</summary>
        public static string BuildSignature(string fullTypeName, string methodName, params string[] paramTypeNames)
        {
            var sb = new StringBuilder();
            sb.Append(fullTypeName).Append('.').Append(methodName).Append('(');
            for (int i = 0; i < paramTypeNames.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(paramTypeNames[i]);
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}
