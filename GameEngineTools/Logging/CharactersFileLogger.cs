// CharactersFileLogger.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// ILogger implementace delegující character file output na provider.
    /// </summary>
    internal sealed class CharactersFileLogger : ILogger
    {
        #region Privátní pole

        private readonly string _category;
        private readonly CharactersFileLoggerProvider _provider;

        #endregion Privátní pole

        #region Konstrukce

        public CharactersFileLogger(string category, CharactersFileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        #endregion Konstrukce

        #region ILogger

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _provider.Scopes?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            CharacterLogScope? characterScope = null;
            _provider.Scopes?.ForEachScope((scopeObj, _) =>
            {
                if (scopeObj is CharacterLogScope cls)
                {
                    characterScope = cls;
                }
            }, (object?)null);

            _provider.Write(logLevel, _category, eventId, message, exception, characterScope);
        }

        #endregion ILogger

        #region Null scope

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            { }
        }

        #endregion Null scope
    }
}
