using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CodexApiLauncher.Desktop;

internal static class CodexConversationProviderMigration
{
    private const int SqliteOk = 0;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteOpenReadWrite = 0x00000002;
    private const int SqliteOpenCreate = 0x00000004;

    private static readonly Regex ProviderIdPattern = new("^[a-z][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

    public static ConversationProviderMigrationResult Run(
        string sharedHome,
        string providerId,
        IReadOnlyCollection<string>? legacyProviderIds = null)
    {
        if (string.IsNullOrWhiteSpace(sharedHome))
        {
            throw new ArgumentException("共享 CODEX_HOME 不能为空。", nameof(sharedHome));
        }
        var normalizedProviderId = providerId ?? "";
        if (!ProviderIdPattern.IsMatch(normalizedProviderId))
        {
            throw new ArgumentException("统一 provider ID 格式无效。", nameof(providerId));
        }

        var home = Path.GetFullPath(sharedHome.Trim());
        if (!Directory.Exists(home))
        {
            return new ConversationProviderMigrationResult
            {
                Status = "no_database",
                Message = "共享 CODEX_HOME 不存在，暂时没有可迁移的会话数据库。"
            };
        }

        var databases = Directory.GetFiles(home, "state_*.sqlite", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (databases.Length == 0)
        {
            return new ConversationProviderMigrationResult
            {
                Status = "no_database",
                Message = "共享 CODEX_HOME 中没有找到 state_*.sqlite。"
            };
        }

        var legacyIds = (legacyProviderIds ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? "")
            .Where(id => id.Length > 0 && ProviderIdPattern.IsMatch(id) && !string.Equals(id, normalizedProviderId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (legacyIds.Length == 0)
        {
            return new ConversationProviderMigrationResult
            {
                Status = "no_changes",
                Message = "没有记录可归并的旧 launcher provider ID；未修改其他会话。"
            };
        }

        var quotedProviderId = SqlQuote(normalizedProviderId);
        var legacyIdList = string.Join(", ", legacyIds.Select(SqlQuote));
        var backupDirectory = "";
        var databaseCount = 0;
        var threadRowsChanged = 0;
        var externalRowsChanged = 0;

        foreach (var databasePath in databases)
        {
            using var database = NativeSqliteDatabase.Open(databasePath);
            database.Execute("PRAGMA busy_timeout = 2500;");

            var hasThreads = database.QueryInt(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'threads';") > 0;
            var threadRows = hasThreads
                ? database.QueryInt($"SELECT COUNT(*) FROM threads WHERE model_provider IN ({legacyIdList});")
                : 0;
            var hasExternalImports = database.QueryInt(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'external_agent_config_imports';") > 0;
            var externalRows = hasExternalImports
                ? database.QueryInt($"SELECT COUNT(*) FROM external_agent_config_imports WHERE provider_id IN ({legacyIdList});")
                : 0;

            if (threadRows == 0 && externalRows == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(backupDirectory))
            {
                backupDirectory = CreateBackup(home, databases);
            }

            database.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (threadRows > 0)
                {
                    database.Execute(
                        $"UPDATE threads SET model_provider = {quotedProviderId} WHERE model_provider IN ({legacyIdList});");
                    threadRowsChanged += database.Changes;
                }

                if (externalRows > 0)
                {
                    database.Execute(
                        $"UPDATE external_agent_config_imports SET provider_id = {quotedProviderId} WHERE provider_id IN ({legacyIdList});");
                    externalRowsChanged += database.Changes;
                }

                database.Execute("COMMIT;");
            }
            catch
            {
                database.TryRollback();
                throw;
            }

            databaseCount++;
        }

        if (threadRowsChanged == 0 && externalRowsChanged == 0)
        {
            return new ConversationProviderMigrationResult
            {
                Status = "no_changes",
                DatabaseCount = 0,
                Message = "会话数据库已经使用统一 provider 身份，没有需要修改的记录。"
            };
        }

        return new ConversationProviderMigrationResult
        {
            Status = "migrated",
            DatabaseCount = databaseCount,
            ThreadRowsChanged = threadRowsChanged,
            ExternalRowsChanged = externalRowsChanged,
            BackupDirectory = backupDirectory,
            Message = "历史会话 provider 身份已统一；原数据库文件已备份。"
        };
    }

    private static string CreateBackup(string home, IReadOnlyList<string> databases)
    {
        var backupDirectory = Path.Combine(
            home,
            "backups",
            $"provider-identity-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backupDirectory);

        foreach (var databasePath in databases)
        {
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Copy(path, Path.Combine(backupDirectory, Path.GetFileName(path)), overwrite: false);
                }
            }
        }

        return backupDirectory;
    }

    private static string SqlQuote(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    internal sealed class NativeSqliteDatabase : IDisposable
    {
        private IntPtr handle;

        private NativeSqliteDatabase(IntPtr handle)
        {
            this.handle = handle;
        }

        public int Changes => sqlite3_changes(handle);

        public static NativeSqliteDatabase Open(string path, bool create = false)
        {
            var flags = SqliteOpenReadWrite | (create ? SqliteOpenCreate : 0);
            var result = sqlite3_open_v2(path, out var handle, flags, IntPtr.Zero);
            if (result != SqliteOk)
            {
                var message = GetError(handle, result);
                if (handle != IntPtr.Zero)
                {
                    sqlite3_close(handle);
                }
                throw new InvalidOperationException($"无法打开 Codex 会话数据库: {message}");
            }

            return new NativeSqliteDatabase(handle);
        }

        public void Execute(string sql)
        {
            IntPtr errorMessage;
            var result = sqlite3_exec(handle, sql, IntPtr.Zero, IntPtr.Zero, out errorMessage);
            if (result != SqliteOk)
            {
                var message = errorMessage != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(errorMessage) ?? "未知 SQLite 错误"
                    : GetError(handle, result);
                if (errorMessage != IntPtr.Zero)
                {
                    sqlite3_free(errorMessage);
                }

                if (result is SqliteBusy or SqliteLocked)
                {
                    throw new InvalidOperationException("Codex 会话数据库正在被运行中的 Codex 使用。请先关闭 Codex 终端和桌面端，再重试历史会话归并。", new IOException(message));
                }

                throw new InvalidOperationException($"SQLite 操作失败: {message}");
            }
        }

        public int QueryInt(string sql)
        {
            var value = 0;
            ExecCallback callback = (_, columnCount, values, _) =>
            {
                if (columnCount > 0 && values != IntPtr.Zero)
                {
                    var firstValue = Marshal.ReadIntPtr(values);
                    if (firstValue != IntPtr.Zero && int.TryParse(Marshal.PtrToStringUTF8(firstValue), out var parsed))
                    {
                        value = parsed;
                    }
                }

                return 0;
            };

            IntPtr errorMessage;
            var callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            var result = sqlite3_exec(handle, sql, callbackPointer, IntPtr.Zero, out errorMessage);
            GC.KeepAlive(callback);
            if (result != SqliteOk)
            {
                var message = errorMessage != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(errorMessage) ?? "未知 SQLite 错误"
                    : GetError(handle, result);
                if (errorMessage != IntPtr.Zero)
                {
                    sqlite3_free(errorMessage);
                }
                throw new InvalidOperationException($"SQLite 查询失败: {message}");
            }

            return value;
        }

        public void TryRollback()
        {
            try
            {
                Execute("ROLLBACK;");
            }
            catch
            {
                // Preserve the original migration failure.
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                sqlite3_close(handle);
                handle = IntPtr.Zero;
            }
        }

        private static string GetError(IntPtr database, int result)
        {
            if (database != IntPtr.Zero)
            {
                var pointer = sqlite3_errmsg(database);
                if (pointer != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUTF8(pointer) ?? $"SQLite 错误码 {result}";
                }
            }

            return $"SQLite 错误码 {result}";
        }

        private delegate int ExecCallback(IntPtr context, int columnCount, IntPtr values, IntPtr names);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            out IntPtr database,
            int flags,
            IntPtr vfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            IntPtr callback,
            IntPtr context,
            out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr pointer);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_changes(IntPtr database);
    }
}

internal sealed class ConversationProviderMigrationResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int DatabaseCount { get; set; }
    public int ThreadRowsChanged { get; set; }
    public int ExternalRowsChanged { get; set; }
    public string BackupDirectory { get; set; } = "";
}
