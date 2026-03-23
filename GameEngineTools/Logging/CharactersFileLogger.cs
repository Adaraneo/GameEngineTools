// CharactersFileLogger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using System.Collections.Concurrent;
    using System.Text;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Vlastní file logger provider pro GameEngineTools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Každá log zpráva se zapisuje na dvě místa simultánně:
    /// <list type="bullet">
    ///   <item><b>Characters.log</b> — globální soubor se všemi zprávami.</item>
    ///   <item><b>Person/{guid}/{Engine}.log</b> — per-postava, per-engine soubor,
    ///         pokud byla zpráva vyvolána uvnitř <see cref="CharacterLogScope"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Routing do per-engine souborů funguje přes <see cref="CharacterLogScope"/>,
    /// který se nastaví přes <c>_log.BeginScope(new CharacterLogScope(id, engineName))</c>.
    /// Provider extrahuje scope <b>přímou typovou kontrolou</b> — žádné string parsování.
    /// </para>
    /// <para>
    /// <b>Flush strategie:</b> <see cref="StreamWriter.AutoFlush"/> je záměrně vypnuto.
    /// Flush se provádí periodicky přes <see cref="FlushAll"/> nebo při <see cref="Dispose"/>.
    /// Tím se eliminuje I/O blokování uvnitř simulačního ticku.
    /// </para>
    /// </remarks>
    internal sealed class CharactersFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        #region Privátní pole

        private readonly CharactersFileLoggerOptions _opt;

        /// <summary>Zámek pouze pro zápis do StreamWriterů — Flush probíhá mimo lock.</summary>
        private readonly object _writeLock = new();

        /// <summary>Globální soubor Characters.log — zachytává vše.</summary>
        private readonly StreamWriter _globalWriter;

        /// <summary>Per-engine soubory indexované cestou k souboru.</summary>
        private readonly ConcurrentDictionary<string, StreamWriter> _engineWriters = new();

        private bool _disposed;
        private IExternalScopeProvider? _scopes;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Inicializuje provider a vytvoří globální log soubor.
        /// </summary>
        /// <param name="opt">Konfigurace logování (cesta, úroveň, UTC timestamps).</param>
        public CharactersFileLoggerProvider(CharactersFileLoggerOptions opt)
        {
            _opt = opt;
            Directory.CreateDirectory(_opt.LogsDirectoryPath);

            _globalWriter = new StreamWriter(
                new FileStream(
                    Path.Combine(_opt.LogsDirectoryPath, "Characters.log"),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = false,  // Flush řídíme ručně — viz FlushAll() a Dispose()
                NewLine = "\n"
            };
        }

        #endregion Konstruktor

        #region ILoggerProvider

        /// <summary>
        /// Vytvoří logger pro danou kategorii (typicky název třídy).
        /// </summary>
        public ILogger CreateLogger(string categoryName) => new CharactersFileLogger(categoryName, this);

        #endregion ILoggerProvider

        #region ISupportExternalScope

        /// <inheritdoc/>
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        #endregion ISupportExternalScope

        #region Veřejné metody

        /// <summary>
        /// Vyflushuje všechny otevřené soubory na disk.
        /// Volej na konci každého simulačního ticku nebo dle potřeby.
        /// </summary>
        /// <remarks>
        /// Tato metoda záměrně provádí I/O <b>mimo</b> simulační smyčku —
        /// nikdy ji nevolej uvnitř <see cref="Write"/>.
        /// </remarks>
        public void FlushAll()
        {
            if (_disposed) return;

            lock (_writeLock)
            {
                _globalWriter.Flush();
                foreach (var w in _engineWriters.Values)
                    w.Flush();
            }
        }

        #endregion Veřejné metody

        #region Interní zápis

        /// <summary>
        /// Určuje zda je daná log úroveň povolena dle konfigurace.
        /// </summary>
        internal bool IsEnabled(LogLevel level) => level >= _opt.MinLevel;

        /// <summary>
        /// Zapíše řádek do globálního souboru a případně do per-engine souboru postavy.
        /// </summary>
        /// <param name="level">Úroveň logu.</param>
        /// <param name="category">Kategorie (název třídy).</param>
        /// <param name="eventId">Identifikátor události.</param>
        /// <param name="message">Naformátovaná zpráva.</param>
        /// <param name="ex">Výjimka, nebo <c>null</c>.</param>
        /// <param name="scope">
        /// Scope postavy extrahovaný z <see cref="IExternalScopeProvider"/>,
        /// nebo <c>null</c> pokud zpráva nepochází z kontextu postavy.
        /// </param>
        internal void Write(
            LogLevel level,
            string category,
            EventId eventId,
            string message,
            Exception? ex,
            CharacterLogScope? scope)
        {
            if (_disposed) return;

            var line = BuildLine(level, category, eventId, message, ex);

            lock (_writeLock)
            {
                // 1. Vždy zapíše do globálního souboru
                _globalWriter.WriteLine(line);

                // 2. Pokud zpráva pochází z CharacterLogScope → zapíše do per-engine souboru
                if (scope.HasValue)
                {
                    var w = GetEngineWriter(scope.Value);
                    w.WriteLine(line);
                }
            }
        }

        #endregion Interní zápis

        #region Privátní pomocné metody

        /// <summary>
        /// Sestaví formátovaný řetězec log záznamu.
        /// </summary>
        /// <param name="level">Úroveň logu.</param>
        /// <param name="category">Kategorie loggeru.</param>
        /// <param name="eventId">ID události (vypisuje se jen pokud != 0).</param>
        /// <param name="message">Tělo zprávy.</param>
        /// <param name="ex">Výjimka nebo <c>null</c>.</param>
        /// <returns>Jeden řádek vhodný pro zápis do souboru.</returns>
        private string BuildLine(LogLevel level, string category, EventId eventId, string message, Exception? ex)
        {
            var ts = _opt.UseUtcTimestamps ? DateTimeOffset.UtcNow : DateTimeOffset.Now;

            var sb = new StringBuilder()
                .Append(ts.ToString("yyyy-MM-ddTHH:mm:ss.fffK"))
                .Append($" [{WDateTime.Now.ToString()}]")
                .Append(" [").Append(level.ToString()[..3].ToUpperInvariant()).Append("] ")
                .Append(category);

            if (eventId.Id != 0)
                sb.Append(" (").Append(eventId.Id).Append(')');

            sb.Append(" :: ").Append(message);

            if (ex is not null)
            {
                sb.Append(" | ex: ")
                  .Append(ex.GetType().Name)
                  .Append(": ")
                  .Append(ex.Message)
                  .AppendLine()
                  .Append(ex.StackTrace);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Vrátí (nebo vytvoří) <see cref="StreamWriter"/> pro daný scope postavy.
        /// Cesta k souboru: <c>Person/{guid}/{Engine}.log</c>
        /// </summary>
        /// <remarks>
        /// Pokud <see cref="CharacterLogScope.Subsystem"/> je prázdný,
        /// použije se fallback soubor <c>person.log</c>.
        /// </remarks>
        /// <param name="scope">Scope identifikující postavu a engine.</param>
        private StreamWriter GetEngineWriter(CharacterLogScope scope)
        {
            var personDir = Path.Combine(
                _opt.LogsDirectoryPath,
                "Person",
                scope.PersonId.ToString());

            var filename = string.IsNullOrEmpty(scope.Subsystem)
                ? "person.log"
                : $"{scope.Subsystem}.log";

            var path = Path.Combine(personDir, filename);

            return _engineWriters.GetOrAdd(path, p =>
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

        #endregion Privátní pomocné metody

        #region IDisposable

        /// <summary>
        /// Flushuje a uvolní všechny otevřené soubory.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_writeLock)
            {
                foreach (var w in _engineWriters.Values)
                {
                    w.Flush();
                    w.Dispose();
                }

                _globalWriter.Flush();
                _globalWriter.Dispose();
            }
        }

        #endregion IDisposable

        #region Vnitřní třída — CharactersFileLogger

        /// <summary>
        /// Interní implementace <see cref="ILogger"/> pro jeden categoryName.
        /// Deleguje veškerou práci na <see cref="CharactersFileLoggerProvider"/>.
        /// </summary>
        private sealed class CharactersFileLogger : ILogger
        {
            #region Privátní pole

            private readonly string _category;
            private readonly CharactersFileLoggerProvider _provider;

            #endregion Privátní pole

            #region Konstruktor

            /// <summary>
            /// Vytvoří logger pro danou kategorii.
            /// </summary>
            /// <param name="category">Název kategorie (typicky <c>typeof(T).FullName</c>).</param>
            /// <param name="provider">Rodičovský provider zajišťující zápis.</param>
            public CharactersFileLogger(string category, CharactersFileLoggerProvider provider)
            {
                _category = category;
                _provider = provider;
            }

            #endregion Konstruktor

            #region ILogger

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                => _provider._scopes?.Push(state) ?? NullScope.Instance;

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var message = formatter(state, exception);

                // Extrahuj CharacterLogScope přímou typovou kontrolou — žádné string parsování
                CharacterLogScope? characterScope = null;
                _provider._scopes?.ForEachScope((scopeObj, _) =>
                {
                    if (scopeObj is CharacterLogScope cls)
                        characterScope = cls;
                }, (object?)null);

                _provider.Write(logLevel, _category, eventId, message, exception, characterScope);
            }

            #endregion ILogger

            #region Null scope

            /// <summary>
            /// Prázdný scope — vrátí se pokud <see cref="IExternalScopeProvider"/> není nastaven.
            /// </summary>
            private sealed class NullScope : IDisposable
            {
                /// <summary>Sdílená singleton instance — nemusíme alokovat novou pro každé volání.</summary>
                public static readonly NullScope Instance = new();

                /// <inheritdoc/>
                public void Dispose()
                { }
            }

            #endregion Null scope
        }

        #endregion Vnitřní třída — CharactersFileLogger
    }

    #region Extension metody

    /// <summary>
    /// Extension metody pro registraci <see cref="CharactersFileLoggerProvider"/> do DI.
    /// </summary>
    public static class CharactersFileLoggingBuilderExtensions
    {
        /// <summary>
        /// Přidá <see cref="CharactersFileLoggerProvider"/> do logging pipeline.
        /// </summary>
        /// <param name="builder">Logging builder z DI.</param>
        /// <param name="configure">Volitelná akce pro konfiguraci options.</param>
        /// <returns>Stejný <paramref name="builder"/> pro řetězení.</returns>
        public static ILoggingBuilder AddCharactersFile(
            this ILoggingBuilder builder,
            Action<CharactersFileLoggerOptions>? configure = null)
        {
            var opt = new CharactersFileLoggerOptions();
            configure?.Invoke(opt);

            var provider = new CharactersFileLoggerProvider(opt);
            builder.AddProvider(provider);

            // Filtr — aplikuje se jen pro tento provider
            builder.AddFilter<CharactersFileLoggerProvider>(level => level >= opt.MinLevel);

            return builder;
        }
    }

    #endregion Extension metody

    #region Options a scope

    /// <summary>
    /// Konfigurační volby pro <see cref="CharactersFileLoggerProvider"/>.
    /// </summary>
    public sealed class CharactersFileLoggerOptions
    {
        /// <summary>
        /// Kořenový adresář pro log soubory.
        /// Výchozí hodnota: <c>logs/Characters</c>.
        /// </summary>
        public string LogsDirectoryPath { get; set; } = Path.Combine("logs", "Characters");

        /// <summary>
        /// Minimální úroveň logování.
        /// Zprávy pod touto úrovní jsou zahozeny.
        /// Výchozí hodnota: <see cref="LogLevel.Information"/>.
        /// </summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Pokud <c>true</c>, timestamps jsou v UTC. Jinak lokální čas.
        /// Výchozí hodnota: <c>true</c>.
        /// </summary>
        public bool UseUtcTimestamps { get; set; } = true;
    }

    /// <summary>
    /// Scope identifikující postavu a engine pro log routing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Použití:
    /// <code>
    /// using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine))))
    /// {
    ///     _log.SleepPromptSent(ctx.Id.Value.ToString(), needRest);
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// Logger provider extrahuje scope <b>přímou typovou kontrolou</b>
    /// (<c>if (s is CharacterLogScope cls)</c>) — žádné <c>ToString()</c> / <c>Split()</c>.
    /// </para>
    /// </remarks>
    public readonly struct CharacterLogScope
    {
        /// <summary>
        /// ID postavy — určuje složku <c>Person/{PersonId}/</c>.
        /// </summary>
        public Guid PersonId { get; }

        /// <summary>
        /// Název enginu — určuje název souboru <c>{Subsystem}.log</c>.
        /// Pokud je prázdný, použije se <c>person.log</c>.
        /// </summary>
        public string Subsystem { get; }

        /// <summary>
        /// Vytvoří scope pro danou postavu a engine.
        /// </summary>
        /// <param name="personId">GUID postavy.</param>
        /// <param name="subsystem">Název enginu (typicky <c>nameof(DefaultBehaviorEngine)</c>).</param>
        public CharacterLogScope(Guid personId, string subsystem)
        {
            PersonId = personId;
            Subsystem = subsystem;
        }

        /// <summary>
        /// Lidsky čitelná reprezentace — používá se v konzolových loggerech a při debug výpisu.
        /// Pro file routing se <b>nepoužívá</b> — viz <see cref="CharactersFileLoggerProvider"/>.
        /// </summary>
        public override string ToString() => $"{PersonId}:{Subsystem}";
    }

    #endregion Options a scope
}
