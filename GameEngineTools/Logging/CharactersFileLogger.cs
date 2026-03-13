// CharactersFileLogger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using System.Collections.Concurrent;
    using System.Text;
    using Microsoft.Extensions.Logging;

    internal sealed class CharactersFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly CharactersFileLoggerOptions _opt;
        private readonly object _sync = new();
        private readonly StreamWriter _writer;
        private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
        private bool _disposed;
        private IExternalScopeProvider? _scopes;

        private sealed class CharactersFileLogger : ILogger
        {
            private readonly string _category;
            private readonly CharactersFileLoggerProvider _provider;

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                { }
            }

            public CharactersFileLogger(string category, CharactersFileLoggerProvider provider)
            {
                _category = category; _provider = provider;
            }

            public IDisposable BeginScope<TState>(TState state) => _provider._scopes?.Push(state) ?? NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                    Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var msg = formatter(state, exception);
                CharacterLogScope? characterScope = null;

                _provider._scopes?.ForEachScope((s, state) =>
                {
                    if (s is CharacterLogScope cls)
                    {
                        characterScope = cls;
                    }
                }, (object?)null);

                _provider.Write(logLevel, _category, eventId, msg, exception, characterScope);
            }
        }

        private StreamWriter GetWriter(string path)
        {
            return _writers.GetOrAdd(path, p =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);

                return new StreamWriter(
                    new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = false,
                    NewLine = "\n"
                };
            });
        }

        internal bool IsEnabled(LogLevel level) => level >= _opt.MinLevel;

        internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? ex, CharacterLogScope? characterScope)
        {
            if (_disposed)
            {
                return;
            }

            var ts = _opt.UseUtcTimestamps ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
            var sb = new StringBuilder()
                .Append(ts.ToString("yyyy-MM-ddTHH:mm:ss.fffK"))
                .Append(" [").Append(level).Append("] ")
                .Append(category);

            if (eventId.Id != 0)
            {
                sb.Append(" (").Append(eventId.Id).Append(')');
            }

            sb.Append(" :: ").Append(message);

            if (ex is not null)
            {
                sb.Append(" | ex: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message)
                  .AppendLine().Append(ex.StackTrace);
            }

            var line = sb.ToString();

            lock (_sync)
            {
                _writer.WriteLine(line);

                if (characterScope.HasValue)
                {
                    var personId = characterScope.Value.PersonId;
                    var subsystem = characterScope.Value.Subsystem;
                    var file = string.IsNullOrEmpty(subsystem)
                        ? Path.Combine(_opt.LogsDirectoryPath, "Person", personId.ToString(), "person.log")
                        : Path.Combine(_opt.LogsDirectoryPath, "Person", personId.ToString(), $"{subsystem}.log");

                    var w = GetWriter(file);
                    w.WriteLine(line);
                }
            }
        }

        public CharactersFileLoggerProvider(CharactersFileLoggerOptions opt)
        {
            _opt = opt;
            Directory.CreateDirectory(_opt.LogsDirectoryPath);
            _writer = new StreamWriter(new FileStream(Path.Combine(_opt.LogsDirectoryPath, "Characters.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = false,
                NewLine = "\n"
            };
        }

        public ILogger CreateLogger(string categoryName) => new CharactersFileLogger(categoryName, this);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_sync)
            {
                foreach (var pw in _writers.Values)
                {
                    pw.Flush();
                    pw.Dispose();
                }

                _writer.Flush();
                _writer.Dispose();
            }
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    }

    public static class CharactersFileLoggingBuilderExtensions
    {
        public static ILoggingBuilder AddCharactersFile(
            this ILoggingBuilder builder,
            Action<CharactersFileLoggerOptions>? configure = null)
        {
            var opt = new CharactersFileLoggerOptions();
            configure?.Invoke(opt);
            var provider = new CharactersFileLoggerProvider(opt);
            builder.AddProvider(provider);

            // Volitelné: filtr jen pro tento provider (jemné ladění)
            builder.AddFilter<CharactersFileLoggerProvider>(level => level >= opt.MinLevel);

            return builder;
        }
    }

    public sealed class CharactersFileLoggerOptions
    {
        public string LogsDirectoryPath { get; set; } = Path.Combine("logs", "Characters");
        public LogLevel MinLevel { get; set; } = LogLevel.Information;
        public bool UseUtcTimestamps { get; set; } = true;
    }

    public readonly struct CharacterLogScope
    {
        public Guid PersonId { get; }
        public string Subsystem { get; }

        public CharacterLogScope(Guid personId, string subsystem)
        {
            PersonId = personId;
            Subsystem = subsystem;
        }

        public override string ToString()
            => $"{PersonId}:{Subsystem}";
    }
}
