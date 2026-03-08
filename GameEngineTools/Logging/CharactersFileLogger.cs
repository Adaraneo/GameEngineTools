// CharactersFileLogger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using System.Text;
    using Microsoft.Extensions.Logging;

    internal sealed class CharactersFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly CharactersFileLoggerOptions _opt;
        private readonly object _sync = new();
        private readonly StreamWriter _writer;
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
                List<string>? scopes = null;
                _provider._scopes?.ForEachScope((s, list) =>
                {
                    (list ??= new()).Add(s?.ToString() ?? string.Empty);
                }, scopes);
                _provider.Write(logLevel, _category, eventId, msg, exception, scopes);
            }
        }

        internal bool IsEnabled(LogLevel level) => level >= _opt.MinLevel;

        internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? ex, List<string>? scopes)
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

            if (scopes is { Count: > 0 })
            {
                sb.Append(" | scopes: ").Append(string.Join(" > ", scopes));
            }

            if (ex is not null)
            {
                sb.Append(" | ex: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message)
                  .AppendLine().Append(ex.StackTrace);
            }

            lock (_sync)
            {
                _writer.WriteLine(sb.ToString());
                _writer.Flush();
            }
        }

        public CharactersFileLoggerProvider(CharactersFileLoggerOptions opt)
        {
            _opt = opt;
            Directory.CreateDirectory(Path.GetDirectoryName(_opt.FilePath)!);
            // Append, AutoFlush off (lepší výkon), UTF8 bez BOM
            _writer = new StreamWriter(new FileStream(_opt.FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
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
        public string FilePath { get; set; } = Path.Combine("logs", "Characters", "characters.log");
        public LogLevel MinLevel { get; set; } = LogLevel.Information;
        public bool UseUtcTimestamps { get; set; } = true;
    }
}
