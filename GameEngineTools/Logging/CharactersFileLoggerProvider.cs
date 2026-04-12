// CharactersFileLoggerProvider.cs
// Copyright (c) 50PSoftware

using System.Collections.Concurrent;
using System.Text;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// File logger provider pro character logy.
    /// </summary>
    /// <remarks>
    /// Zachovává topologii globálního logu a volitelného per-person/per-subsystem mirroru.
    /// Textové logy zůstávají čitelné pro člověka, JSONL companion soubory jsou určené pro resolver.
    /// </remarks>
    internal sealed class CharactersFileLoggerProvider : ILoggerProvider, ISupportExternalScope, ICharactersLogControl
    {
        #region Privátní pole

        private readonly CharactersFileLoggerOptions _opt;
        private readonly object _writeLock = new();
        private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();

        private long _nextEventInstanceId;
        private bool _disposed;
        private IExternalScopeProvider? _scopes;

        #endregion Privátní pole

        #region Konstrukce

        public CharactersFileLoggerProvider(CharactersFileLoggerOptions opt)
        {
            _opt = opt;

            if (_opt.WriteTextLogs || _opt.WriteJsonLines)
            {
                Directory.CreateDirectory(_opt.LogsDirectoryPath);
            }
        }

        #endregion Konstrukce

        #region ILoggerProvider

        public ILogger CreateLogger(string categoryName) => new CharactersFileLogger(categoryName, this);

        #endregion ILoggerProvider

        #region ISupportExternalScope

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        #endregion ISupportExternalScope

        #region ICharactersLogControl

        public void FlushAll()
        {
            if (_disposed)
            {
                return;
            }

            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var writer in _writers.Values)
                {
                    writer.Flush();
                }
            }
        }

        #endregion ICharactersLogControl

        #region Interní API

        internal IExternalScopeProvider? Scopes => _scopes;

        internal bool IsEnabled(LogLevel level) => !_disposed && level >= _opt.MinLevel;

        internal void Write(
            LogLevel level,
            string category,
            EventId eventId,
            string message,
            Exception? ex,
            CharacterLogScope? scope)
        {
            if (_disposed || (!_opt.WriteTextLogs && !_opt.WriteJsonLines))
            {
                return;
            }

            var entry = BuildEntry(level, category, eventId, message, ex, scope);

            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                WriteEntry(GlobalPath(".log"), GlobalPath(".jsonl"), entry);

                if (scope.HasValue && _opt.MirrorMode == CharactersLogMirrorMode.GlobalAndScoped)
                {
                    WriteEntry(ScopedPath(scope.Value, ".log"), ScopedPath(scope.Value, ".jsonl"), entry);
                }
            }
        }

        #endregion Interní API

        #region Privátní metody

        private CharactersLogEntry BuildEntry(
            LogLevel level,
            string category,
            EventId eventId,
            string message,
            Exception? ex,
            CharacterLogScope? scope)
        {
            var now = _opt.UseUtcTimestamps ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
            var worldTimeText = _opt.WorldTimeTextAccessor?.Invoke() ?? WDateTime.Now.ToString();

            return new CharactersLogEntry
            {
                EventInstanceId = Interlocked.Increment(ref _nextEventInstanceId),
                RealTimestamp = now,
                WorldTimeText = worldTimeText,
                Level = ShortLevel(level),
                Category = category,
                EventId = eventId.Id,
                Message = message,
                ExceptionType = ex?.GetType().FullName,
                ExceptionMessage = ex?.Message,
                StackTrace = ex?.StackTrace,
                PersonId = scope?.PersonId,
                Subsystem = scope?.Subsystem,
                CorrelationId = scope?.CorrelationId,
                InteractionId = scope?.InteractionId,
                DecisionId = scope?.DecisionId,
                RelatedPersonId = scope?.RelatedPersonId,
                LocationId = scope?.LocationId,
                TickKey = scope?.TickKey,
                IsScoped = scope.HasValue
            };
        }

        private void WriteEntry(string textPath, string jsonPath, CharactersLogEntry entry)
        {
            if (_opt.WriteTextLogs)
            {
                GetWriter(textPath).WriteLine(FormatText(entry));
            }

            if (_opt.WriteJsonLines)
            {
                GetWriter(jsonPath).WriteLine(CharactersJsonLogFormatter.Format(entry));
            }
        }

        private StreamWriter GetWriter(string path)
            => _writers.GetOrAdd(path, p =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);

                return new StreamWriter(new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = false,
                    NewLine = "\n"
                };
            });

        private string GlobalPath(string extension)
            => Path.Combine(_opt.LogsDirectoryPath, "Characters" + extension);

        private string ScopedPath(CharacterLogScope scope, string extension)
        {
            var filename = CharactersLogPathSanitizer.SanitizeSubsystemFileName(scope.Subsystem) + extension;
            return Path.Combine(_opt.LogsDirectoryPath, "Person", scope.PersonId.ToString(), filename);
        }

        private static string FormatText(CharactersLogEntry entry)
        {
            var sb = new StringBuilder()
                .Append(entry.RealTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffK"))
                .Append(" [W:").Append(entry.WorldTimeText).Append(']')
                .Append(" [Seq:").Append(entry.EventInstanceId).Append(']')
                .Append(" [").Append(entry.Level).Append(']');

            if (entry.PersonId.HasValue)
            {
                sb.Append(" [P:").Append(entry.PersonId.Value).Append(']');
            }

            if (!string.IsNullOrWhiteSpace(entry.Subsystem))
            {
                sb.Append(" [S:").Append(entry.Subsystem).Append(']');
            }

            if (!string.IsNullOrWhiteSpace(entry.CorrelationId))
            {
                sb.Append(" [Corr:").Append(entry.CorrelationId).Append(']');
            }

            if (entry.RelatedPersonId.HasValue)
            {
                sb.Append(" [Rel:").Append(entry.RelatedPersonId.Value).Append(']');
            }

            if (!string.IsNullOrWhiteSpace(entry.TickKey))
            {
                sb.Append(" [Tick:").Append(entry.TickKey).Append(']');
            }

            sb.Append(' ').Append(entry.Category);

            if (entry.EventId != 0)
            {
                sb.Append(" (").Append(entry.EventId).Append(')');
            }

            sb.Append(" :: ").Append(entry.Message);

            if (!string.IsNullOrWhiteSpace(entry.ExceptionType))
            {
                sb.Append(" | ex: ")
                  .Append(entry.ExceptionType)
                  .Append(": ")
                  .Append(entry.ExceptionMessage);

                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    sb.AppendLine().Append(entry.StackTrace);
                }
            }

            return sb.ToString();
        }

        private static string ShortLevel(LogLevel level)
            => level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                LogLevel.None => "NON",
                _ => level.ToString()[..Math.Min(3, level.ToString().Length)].ToUpperInvariant()
            };

        #endregion Privátní metody

        #region IDisposable

        public void Dispose()
        {
            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                foreach (var writer in _writers.Values)
                {
                    writer.Flush();
                    writer.Dispose();
                }

                _writers.Clear();
            }
        }

        #endregion IDisposable
    }
}
