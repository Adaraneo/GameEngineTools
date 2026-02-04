using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameTester.Extensions
{
    public static class HostingTestExtensions
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        public static async Task StartHostedServicesAsync(
            this IServiceProvider sp,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            foreach (var hs in sp.GetServices<IHostedService>())
                await hs.StartAsync(cts.Token);
        }

        public static async Task StopHostedServicesAsync(
            this IServiceProvider sp,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            // pokud na pořadí záleží, otáčej
            var hosted = sp.GetServices<IHostedService>().ToList();
            hosted.Reverse();

            foreach (var hs in hosted)
            {
                try { await hs.StopAsync(cts.Token); }
                catch { /* best-effort v testech */ }
            }
        }
    }

}
