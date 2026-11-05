// ConfigDefaultsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Psychology;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>A config built with <c>new X()</c> must equal one built from the record's own parameter defaults.</summary>
    [TestClass]
    public sealed class ConfigDefaultsTests : TestBase
    {
        [TestMethod]
        public void ParameterlessConfigs_HaveTheSameDefaultsAsTheirPrimaryConstructor()
        {
            var assembly = typeof(PsychologyConfig).Assembly;
            var offenders = new List<string>();

            foreach (var type in assembly.GetTypes().Where(t => t.IsClass && t.IsSealed && t.IsPublic && t.Name.EndsWith("Config")))
            {
                var ctors = type.GetConstructors();
                var parameterless = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
                var primary = ctors.Where(c => c.GetParameters().Length > 0 && c.GetParameters().All(p => p.HasDefaultValue))
                    .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (parameterless is null || primary is null) continue;

                var fromParameterless = parameterless.Invoke(null);
                var fromPrimary = primary.Invoke(primary.GetParameters().Select(p => p.DefaultValue).ToArray());
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))))
                {
                    if (!Equals(property.GetValue(fromParameterless), property.GetValue(fromPrimary)))
                        offenders.Add($"{type.Name}.{property.Name}: parameterless={property.GetValue(fromParameterless)} primary={property.GetValue(fromPrimary)}");
                }
            }

            Assert.AreEqual(0, offenders.Count, "Defaults disagree:\n" + string.Join("\n", offenders.Take(20)));
        }

        [TestMethod]
        public void BoundPsychologyConfig_KeepsTheRecordDefaults_ForKeysMissingFromAppsettings()
        {
            var bound = ServiceProvider.GetRequiredService<IOptions<PsychologyConfig>>().Value;
            var defaults = new PsychologyConfig();

            Assert.AreEqual(defaults.PmddEstradiolWithdrawalRef, bound.PmddEstradiolWithdrawalRef, "A key absent from the JSON falls back to the real default, not to a shifted one.");
            Assert.AreEqual(70.0, bound.PmddEstradiolWithdrawalRef);
            Assert.AreEqual(6.0, bound.PrivacyMismatchStressWeight, "A key present in the JSON is bound from it.");
        }
    }
}
