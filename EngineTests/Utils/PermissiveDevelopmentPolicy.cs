// PermissiveDevelopmentPolicy.cs
// Copyright (c) 50PSoftware

namespace EngineTests.Utils
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Testovací <see cref="ICharacterDevelopmentPolicy"/>, která vždy vrací
    /// <see cref="StadiumType.Adult"/> a povoluje všechny akce — bez ohledu na
    /// <see cref="IHumanContext.Identity"/>.
    /// </summary>
    /// <remarks>
    /// Behaviorální testy běžící u světové epochy (rok 1) nemohou sestavit dospělé
    /// datum narození (<see cref="WDateOnly"/> nesmí být záporné), takže by každý
    /// kontext vyšel jako věk 0 = <see cref="StadiumType.Baby"/> a Work by se
    /// zablokoval. Tyto testy netestují věkové gatování — proto se z něj explicitně
    /// odhlašují touto policy místo spoléhání na (nyní vyhazující) fallback
    /// <see cref="DefaultCharacterDevelopmentPolicy"/> při chybějící identitě.
    /// </remarks>
    public sealed class PermissiveDevelopmentPolicy : ICharacterDevelopmentPolicy
    {
        public StadiumType ResolveStadium(IHumanContext context, WDateTime now) => StadiumType.Adult;

        public bool AllowsAction(StadiumType stadium, string actionName) => true;

        public bool AllowsAdultIntimacy(StadiumType stadium) => true;
    }
}
