using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Arriba.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.TraceListener;
using Microsoft.Diagnostics.EventFlow;
using Microsoft.Diagnostics.EventFlow.ApplicationInsights;
using Microsoft.Diagnostics.EventFlow.HealthReporters;
using Microsoft.Diagnostics.EventFlow.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace Arriba
{
    public static class ApplicationInsightsExtensions
    {
        public static void AddApplicationInsights(this IServiceCollection services, IApplicationInsightsConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config?.InstrumentationKey))
            {
                Trace.WriteLine("Enabling Application Insights Telemetry");

                services.AddApplicationInsightsTelemetry((options) =>
                {
                    options.ApplicationVersion = config.AppConfig.ApplicationVersion;
                    options.InstrumentationKey = config.InstrumentationKey;
                });

                services.AddSingleton<ITelemetryInitializer>(new ReportServiceNameProcessor(config.AppConfig.ServiceName));
                services.UseEventFlow();
            }
        }

        public static void UseAppInsightsTraceListener(this IApplicationInsightsConfiguration config)
        {
            Trace.WriteLine("Registering Application Insights trace listener");
            var listener = new ApplicationInsightsTraceListener(config.InstrumentationKey);
            Trace.Listeners.Add(listener);
        }


        private static void UseEventFlow(this IServiceCollection services)
        {
            var healthReport = new NoOpHealthReporter();
            var aiInput = new ApplicationInsightsInputFactory().CreateItem(null, healthReport);
            var inputs = new IObservable<EventData>[] { aiInput };
            var sinks = new EventSink[]
            {
                new EventSink(new NoOpOutput(), null)
            };

            var p = new DiagnosticPipeline(healthReport, inputs, null, sinks, null, disposeDependencies: true);
            services.AddSingleton<ITelemetryProcessorFactory>(new EventFlowTelemetryProcessorFactory(p));
        }

        private class NoOpHealthReporter : IHealthReporter
        {
            public void Dispose()
            {
            }

            public void ReportHealthy(string description = null, string context = null)
            {
            }

            public void ReportProblem(string description, string context = null)
            {
            }

            public void ReportWarning(string description, string context = null)
            {
            }
        }

        private class NoOpOutput : IOutput
        {
            public Task SendEventsAsync(IReadOnlyCollection<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
