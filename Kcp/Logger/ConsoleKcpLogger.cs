using System;
using Kcp.Interface;

namespace Kcp.Logger
{
    public sealed class ConsoleKcpLogger : IKcpLogger
    {
        public void LogInfo(string message) => Console.WriteLine($"[KCP INFO] {DateTime.Now:O} | {message}");
        public void LogWarning(string message) => Console.WriteLine($"[KCP WARN] {DateTime.Now:O} | {message}");
        public void LogError(string message) => Console.WriteLine($"[KCP ERROR] {DateTime.Now:O} | {message}");
        public void LogDebug(string message) => Console.WriteLine($"[KCP DEBUG] {DateTime.Now:O} | {message}");
    }
}