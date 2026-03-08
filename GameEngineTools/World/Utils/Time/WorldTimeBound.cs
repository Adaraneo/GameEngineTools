// WorldTimeBound.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Utils.Time
{
    using GameEngineTools.World.Core.Time;

    // ═══════════════════════════════════════════════════════════════════════════
    //  Proč Context-Bound wrappers?
    //
    //  WDateTime / WDateOnly / WTimeOnly / WTimeSpan jsou čisté value types —
    //  drží pouze čísla a neví nic o kalendáři. Veškerá logika závislá na
    //  WorldTimeSpec žije v WorldTimeContext (DI singleton).
    //
    //  Bound wrappers řeší situace kde potřebuješ pracovat s jednou hodnotou
    //  opakovaně — ctx "přivážeš" jednou a pak voláš přirozeně:
    //
    //      var d = dt.Bind(ctx);
    //      var year   = d.Year;                      // žádné ctx parametry
    //      var result = d.AddDays(5).AddHours(2);    // fluent chaining
    //      var text   = d.Format();
    //
    //  Kdykoliv potřebuješ raw hodnotu zpět, funguje implicit conversion:
    //
    //      WDateTime raw = d;    // žádný .Value ani přetypování
    //
    //  Vzor: Context-Bound Wrapper (varianta Facade pro value types)
    // ═══════════════════════════════════════════════════════════════════════════

    #region BoundWDateTime

    /// <summary>
    /// Obal <see cref="WDateTime"/> přivázaný na konkrétní <see cref="WorldTimeContext"/>.
    /// Umožňuje přirozené volání properties a metod bez opakovaného předávání ctx.
    /// </summary>
    /// <remarks>
    /// Použij <see cref="WDateTimeBoundExtensions.Bind"/> pro vstup do bound světa:
    /// <code>
    /// var d = dt.Bind(ctx);
    /// var year = d.Year;
    /// WDateTime raw = d;    // implicit conversion zpět
    /// </code>
    /// </remarks>
    public readonly struct BoundWDateTime
    {
        #region Soukromá pole

        private readonly WDateTime _dt;
        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Přiváže okamžik na daný kontext. Preferuj extension metodu
        /// <see cref="WDateTimeBoundExtensions.Bind"/> pro čitelnější zápis.
        /// </summary>
        /// <param name="dt">Okamžik k přivázání.</param>
        /// <param name="ctx">Kontext s kalendářem a spec.</param>
        public BoundWDateTime(WDateTime dt, WorldTimeContext ctx)
        {
            _dt = dt;
            _ctx = ctx;
        }

        #endregion

        #region Přístup k raw hodnotě

        /// <summary>
        /// Implicitní konverze zpět na raw <see cref="WDateTime"/>.
        /// Umožňuje předat <see cref="BoundWDateTime"/> všude kde se očekává <see cref="WDateTime"/>.
        /// </summary>
        public static implicit operator WDateTime(BoundWDateTime b) => b._dt;

        /// <summary>Raw <see cref="WDateTime"/> bez kontextu.</summary>
        public WDateTime Raw => _dt;

        #endregion

        #region Properties — datum

        /// <summary>Rok okamžiku podle světového kalendáře.</summary>
        public int Year => _ctx.GetParts(_dt).year;

        /// <summary>Měsíc okamžiku (1-based) podle světového kalendáře.</summary>
        public int Month => _ctx.GetParts(_dt).month;

        /// <summary>Den v měsíci (1-based) podle světového kalendáře.</summary>
        public int Day => _ctx.GetParts(_dt).day;

        /// <summary>Den v roce (1-based) podle světového kalendáře.</summary>
        public int DayOfYear => _ctx.GetDayOfYear(_dt);

        #endregion

        #region Properties — čas

        /// <summary>Hodina dne (0-based) podle spec.</summary>
        public int Hour => _ctx.GetParts(_dt).hour;

        /// <summary>Minuta hodiny (0-based) podle spec.</summary>
        public int Minute => _ctx.GetParts(_dt).minute;

        /// <summary>Sekunda minuty (0-based) podle spec.</summary>
        public int Second => _ctx.GetParts(_dt).second;

        #endregion

        #region Properties — složené

        /// <summary>
        /// Datová část okamžiku přivázaná na stejný kontext.
        /// </summary>
        public BoundWDateOnly Date => new(_ctx.GetDate(_dt), _ctx);

        /// <summary>
        /// Časová část okamžiku (čas dne) přivázaná na stejný kontext.
        /// </summary>
        public BoundWTimeOnly Time => new(_ctx.GetTime(_dt), _ctx);

        #endregion

        #region Aritmetika — vrací BoundWDateTime (fluent chaining)

        /// <summary>
        /// Přičte zadaný počet celých dní.
        /// </summary>
        /// <param name="days">Počet dní (může být záporný).</param>
        /// <returns>Nový přivázaný okamžik posunutý o <paramref name="days"/> dní.</returns>
        public BoundWDateTime AddDays(long days)
            => new(_ctx.AddDays(_dt, days), _ctx);

        /// <summary>Přičte zadaný počet celých hodin.</summary>
        /// <param name="hours">Počet hodin (může být záporný).</param>
        public BoundWDateTime AddHours(long hours)
            => new(_ctx.AddHours(_dt, hours), _ctx);

        /// <summary>Přičte zadaný počet celých minut.</summary>
        /// <param name="minutes">Počet minut (může být záporný).</param>
        public BoundWDateTime AddMinutes(long minutes)
            => new(_ctx.AddMinutes(_dt, minutes), _ctx);

        /// <summary>Přičte zadaný počet celých sekund.</summary>
        /// <param name="seconds">Počet sekund (může být záporný).</param>
        public BoundWDateTime AddSeconds(long seconds)
            => new(_ctx.AddSeconds(_dt, seconds), _ctx);

        #endregion

        #region Withery — vrací BoundWDateTime (fluent chaining)

        /// <summary>
        /// Vrátí kopii s nahrazenou datovou složkou. Čas dne zůstane zachován.
        /// </summary>
        /// <param name="date">Nové datum.</param>
        public BoundWDateTime WithDate(WDateOnly date)
            => new(_ctx.WithDate(_dt, date), _ctx);

        /// <summary>
        /// Vrátí kopii s nahrazenou datovou složkou. Čas dne zůstane zachován.
        /// </summary>
        /// <param name="date">Nové datum (přivázaný wrapper).</param>
        public BoundWDateTime WithDate(BoundWDateOnly date)
            => new(_ctx.WithDate(_dt, date), _ctx);

        /// <summary>
        /// Vrátí kopii s nahrazenou časovou složkou. Datum zůstane zachováno.
        /// </summary>
        /// <param name="time">Nový čas dne.</param>
        public BoundWDateTime WithTime(WTimeOnly time)
            => new(_ctx.WithTime(_dt, time), _ctx);

        /// <summary>
        /// Vrátí kopii s nahrazenou časovou složkou. Datum zůstane zachováno.
        /// </summary>
        /// <param name="time">Nový čas dne (přivázaný wrapper).</param>
        public BoundWDateTime WithTime(BoundWTimeOnly time)
            => new(_ctx.WithTime(_dt, time), _ctx);

        #endregion

        #region Formátování

        /// <summary>
        /// Naformátuje okamžik jako <c>YYYY-MM-DDTHH:MM:SS[.subW]</c>.
        /// </summary>
        public string Format() => _ctx.Format(_dt);

        /// <inheritdoc cref="Format()"/>
        public override string ToString() => Format();

        #endregion
    }

    /// <summary>
    /// Entry point pro přivázání <see cref="WDateTime"/> na <see cref="WorldTimeContext"/>.
    /// </summary>
    public static class WDateTimeBoundExtensions
    {
        /// <summary>
        /// Přiváže okamžik na kontext a vrátí <see cref="BoundWDateTime"/>.
        /// </summary>
        /// <param name="dt">Okamžik.</param>
        /// <param name="ctx">Kontext s kalendářem a spec.</param>
        /// <example>
        /// <code>
        /// var d = ctx.Now().Bind(ctx);
        /// int year = d.Year;
        /// WDateTime shifted = d.AddDays(5).AddHours(2);
        /// </code>
        /// </example>
        public static BoundWDateTime Bind(this WDateTime dt, WorldTimeContext ctx)
            => new(dt, ctx);
    }

    #endregion

    #region BoundWDateOnly

    /// <summary>
    /// Obal <see cref="WDateOnly"/> přivázaný na konkrétní <see cref="WorldTimeContext"/>.
    /// </summary>
    public readonly struct BoundWDateOnly
    {
        #region Soukromá pole

        private readonly WDateOnly _date;
        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Přiváže datum na daný kontext. Preferuj extension metodu
        /// <see cref="WDateOnlyBoundExtensions.Bind"/>.
        /// </summary>
        public BoundWDateOnly(WDateOnly date, WorldTimeContext ctx)
        {
            _date = date;
            _ctx = ctx;
        }

        #endregion

        #region Přístup k raw hodnotě

        /// <summary>Implicitní konverze zpět na raw <see cref="WDateOnly"/>.</summary>
        public static implicit operator WDateOnly(BoundWDateOnly b) => b._date;

        /// <summary>Raw <see cref="WDateOnly"/> bez kontextu.</summary>
        public WDateOnly Raw => _date;

        #endregion

        #region Properties

        /// <summary>Rok data podle světového kalendáře.</summary>
        public int Year => _ctx.GetDateParts(_date).year;

        /// <summary>Měsíc data (1-based) podle světového kalendáře.</summary>
        public int Month => _ctx.GetDateParts(_date).month;

        /// <summary>Den v měsíci (1-based) podle světového kalendáře.</summary>
        public int Day => _ctx.GetDateParts(_date).day;

        #endregion

        #region Aritmetika — vrací BoundWDateOnly (fluent chaining)

        /// <summary>
        /// Přičte zadaný počet celých dní.
        /// </summary>
        /// <param name="days">Počet dní (může být záporný).</param>
        public BoundWDateOnly AddDays(long days)
            => new(new WDateOnly(_date.DayIndex + days), _ctx);

        /// <summary>
        /// Přičte zadaný počet měsíců s pinningem na poslední den měsíce.
        /// </summary>
        /// <param name="months">Počet měsíců (může být záporný).</param>
        public BoundWDateOnly AddMonths(int months)
            => new(_ctx.AddMonths(_date, months), _ctx);

        /// <summary>
        /// Přičte zadaný počet let s pinningem na poslední den měsíce.
        /// </summary>
        /// <param name="years">Počet let (může být záporný).</param>
        public BoundWDateOnly AddYears(int years)
            => new(_ctx.AddYears(_date, years), _ctx);

        #endregion

        #region Konverze na BoundWDateTime

        /// <summary>
        /// Kombinuje datum se zadaným časem dne.
        /// </summary>
        /// <param name="time">Čas dne.</param>
        /// <returns>Přivázaný okamžik.</returns>
        public BoundWDateTime At(WTimeOnly time)
            => new(_ctx.At(_date, time), _ctx);

        /// <summary>
        /// Kombinuje datum se zadaným přivázaným časem dne.
        /// </summary>
        /// <param name="time">Přivázaný čas dne.</param>
        public BoundWDateTime At(BoundWTimeOnly time)
            => new(_ctx.At(_date, time), _ctx);

        /// <summary>
        /// Vrátí <see cref="BoundWDateTime"/> na začátku tohoto dne (00:00:00).
        /// </summary>
        public BoundWDateTime StartOfDay()
            => new(_ctx.StartOfDay(_date), _ctx);

        #endregion

        #region Formátování

        /// <summary>
        /// Naformátuje datum jako <c>YYYY-MM-DD</c>.
        /// </summary>
        public string Format() => _ctx.Format(_date);

        /// <inheritdoc cref="Format()"/>
        public override string ToString() => Format();

        #endregion
    }

    /// <summary>
    /// Entry point pro přivázání <see cref="WDateOnly"/> na <see cref="WorldTimeContext"/>.
    /// </summary>
    public static class WDateOnlyBoundExtensions
    {
        /// <summary>
        /// Přiváže datum na kontext a vrátí <see cref="BoundWDateOnly"/>.
        /// </summary>
        /// <param name="date">Datum.</param>
        /// <param name="ctx">Kontext s kalendářem.</param>
        /// <example>
        /// <code>
        /// var d = someDate.Bind(ctx);
        /// var next = d.AddMonths(3).AddDays(1);
        /// WDateOnly raw = d;
        /// </code>
        /// </example>
        public static BoundWDateOnly Bind(this WDateOnly date, WorldTimeContext ctx)
            => new(date, ctx);
    }

    #endregion

    #region BoundWTimeOnly

    /// <summary>
    /// Obal <see cref="WTimeOnly"/> přivázaný na konkrétní <see cref="WorldTimeContext"/>.
    /// </summary>
    public readonly struct BoundWTimeOnly
    {
        #region Soukromá pole

        private readonly WTimeOnly _time;
        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Přiváže čas dne na daný kontext. Preferuj extension metodu
        /// <see cref="WTimeOnlyBoundExtensions.Bind"/>.
        /// </summary>
        public BoundWTimeOnly(WTimeOnly time, WorldTimeContext ctx)
        {
            _time = time;
            _ctx = ctx;
        }

        #endregion

        #region Přístup k raw hodnotě

        /// <summary>Implicitní konverze zpět na raw <see cref="WTimeOnly"/>.</summary>
        public static implicit operator WTimeOnly(BoundWTimeOnly b) => b._time;

        /// <summary>Raw <see cref="WTimeOnly"/> bez kontextu.</summary>
        public WTimeOnly Raw => _time;

        #endregion

        #region Properties

        /// <summary>Hodina dne (0-based).</summary>
        public int Hour => _ctx.GetTimeParts(_time).hour;

        /// <summary>Minuta hodiny (0-based).</summary>
        public int Minute => _ctx.GetTimeParts(_time).minute;

        /// <summary>Sekunda minuty (0-based).</summary>
        public int Second => _ctx.GetTimeParts(_time).second;

        /// <summary>Milisekunda v rámci sekundy (0..999).</summary>
        public int Millisecond => _ctx.GetMillisecond(_time);

        #endregion

        #region Aritmetika — vrací BoundWTimeOnly (fluent chaining, wraparound)

        /// <summary>
        /// Přičte interval. Výsledek se zabalí přes půlnoc (wraparound).
        /// </summary>
        /// <param name="span">Interval k přičtení.</param>
        /// <remarks>Wraparound: 23:00 + 2h = 01:00.</remarks>
        public BoundWTimeOnly Add(WTimeSpan span)
            => new(_ctx.AddTime(_time, span), _ctx);

        /// <summary>Přičte hodiny (wraparound přes půlnoc).</summary>
        /// <param name="hours">Počet hodin (může být záporný).</param>
        public BoundWTimeOnly AddHours(double hours)
            => new(_ctx.AddHours(_time, hours), _ctx);

        /// <summary>Přičte minuty (wraparound přes půlnoc).</summary>
        /// <param name="minutes">Počet minut (může být záporný).</param>
        public BoundWTimeOnly AddMinutes(double minutes)
            => new(_ctx.AddMinutes(_time, minutes), _ctx);

        /// <summary>Přičte sekundy (wraparound přes půlnoc).</summary>
        /// <param name="seconds">Počet sekund (může být záporný).</param>
        public BoundWTimeOnly AddSeconds(double seconds)
            => new(_ctx.AddSeconds(_time, seconds), _ctx);

        /// <summary>
        /// Vrátí kladný interval od <paramref name="other"/> do tohoto času (s wraparoundem).
        /// </summary>
        /// <param name="other">Výchozí čas.</param>
        public BoundWTimeSpan TimeDiff(WTimeOnly other)
            => new(_ctx.TimeDiff(_time, other), _ctx);

        #endregion

        #region Formátování

        /// <summary>Naformátuje čas dne jako <c>HH:MM:SS[.sub]</c>.</summary>
        public string Format() => _ctx.Format(_time);

        /// <inheritdoc cref="Format()"/>
        public override string ToString() => Format();

        #endregion
    }

    /// <summary>
    /// Entry point pro přivázání <see cref="WTimeOnly"/> na <see cref="WorldTimeContext"/>.
    /// </summary>
    public static class WTimeOnlyBoundExtensions
    {
        /// <summary>
        /// Přiváže čas dne na kontext a vrátí <see cref="BoundWTimeOnly"/>.
        /// </summary>
        /// <param name="time">Čas dne.</param>
        /// <param name="ctx">Kontext se spec.</param>
        /// <example>
        /// <code>
        /// var t = someTime.Bind(ctx);
        /// var later = t.AddHours(2).AddMinutes(30);
        /// WTimeOnly raw = t;
        /// </code>
        /// </example>
        public static BoundWTimeOnly Bind(this WTimeOnly time, WorldTimeContext ctx)
            => new(time, ctx);
    }

    #endregion

    #region BoundWTimeSpan

    /// <summary>
    /// Obal <see cref="WTimeSpan"/> přivázaný na konkrétní <see cref="WorldTimeContext"/>.
    /// </summary>
    public readonly struct BoundWTimeSpan
    {
        #region Soukromá pole

        private readonly WTimeSpan _span;
        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Přiváže interval na daný kontext. Preferuj extension metodu
        /// <see cref="WTimeSpanBoundExtensions.Bind"/>.
        /// </summary>
        public BoundWTimeSpan(WTimeSpan span, WorldTimeContext ctx)
        {
            _span = span;
            _ctx = ctx;
        }

        #endregion

        #region Přístup k raw hodnotě

        /// <summary>Implicitní konverze zpět na raw <see cref="WTimeSpan"/>.</summary>
        public static implicit operator WTimeSpan(BoundWTimeSpan b) => b._span;

        /// <summary>Raw <see cref="WTimeSpan"/> bez kontextu.</summary>
        public WTimeSpan Raw => _span;

        #endregion

        #region Properties — konverze na double

        /// <summary>Celkový počet sekund (může být záporný).</summary>
        public double TotalSeconds => _ctx.TotalSeconds(_span);

        /// <summary>Celkový počet minut (může být záporný).</summary>
        public double TotalMinutes => _ctx.TotalMinutes(_span);

        /// <summary>Celkový počet hodin (může být záporný).</summary>
        public double TotalHours => _ctx.TotalHours(_span);

        /// <summary>Celkový počet dní (může být záporný).</summary>
        public double TotalDays => _ctx.TotalDays(_span);

        /// <summary>Absolutní celkový počet sekund (vždy kladné).</summary>
        public double AbsTotalSeconds => _ctx.AbsTotalSeconds(_span);

        /// <summary>Absolutní celkový počet hodin (vždy kladné).</summary>
        public double AbsTotalHours => _ctx.AbsTotalHours(_span);

        /// <summary>Absolutní celkový počet dní (vždy kladné).</summary>
        public double AbsTotalDays => _ctx.AbsTotalDays(_span);

        #endregion

        #region Dekompozice

        /// <summary>
        /// Rozloží interval na dny, hodiny, minuty, sekundy a podtiky.
        /// </summary>
        /// <returns>Tuple (days, hours, minutes, seconds, subTicks).</returns>
        public (long days, int hours, int minutes, int seconds, long subTicks) Deconstruct()
            => _ctx.DeconstructSpan(_span);

        #endregion

        #region Formátování

        /// <summary>
        /// Naformátuje interval jako <c>[-]d.hh:mm:ss[.sub]</c>.
        /// </summary>
        public string Format() => _ctx.Format(_span);

        /// <inheritdoc cref="Format()"/>
        public override string ToString() => Format();

        #endregion
    }

    /// <summary>
    /// Entry point pro přivázání <see cref="WTimeSpan"/> na <see cref="WorldTimeContext"/>.
    /// </summary>
    public static class WTimeSpanBoundExtensions
    {
        /// <summary>
        /// Přiváže interval na kontext a vrátí <see cref="BoundWTimeSpan"/>.
        /// </summary>
        /// <param name="span">Interval.</param>
        /// <param name="ctx">Kontext se spec.</param>
        /// <example>
        /// <code>
        /// var s = someSpan.Bind(ctx);
        /// double hours = s.TotalHours;
        /// var (days, hh, mm, ss, sub) = s.Deconstruct();
        /// </code>
        /// </example>
        public static BoundWTimeSpan Bind(this WTimeSpan span, WorldTimeContext ctx)
            => new(span, ctx);
    }

    #endregion
}
