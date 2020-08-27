using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arriba.Telemetry;
using Microsoft.ApplicationInsights.AspNetCore;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.TraceListener;
using Microsoft.Diagnostics.EventFlow;
using Microsoft.Diagnostics.EventFlow.ApplicationInsights;
using Microsoft.Diagnostics.EventFlow.Inputs;
using Microsoft.Extensions.DependencyInjection;
using Spotsoft.Diagnostics.EventFlow.Outputs.Splunk;
using Spotsoft.Diagnostics.EventFlow.Outputs.Splunk.Configuration;

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
            services.AddTransient<IHealthReporter, NoOpHealthReporter>();
            services.AddTransient<IOutput, NoOpOutput>();
            
            var config = new SplunkOutputConfiguration()
            {
                Host = "localhost",
                ServiceBaseAddress = "http://localhost:8088",
                AuthenticationToken = "TOKEN HERE"
            };

            services.AddTransient<IOutput>( p=> new SplunkOutputFactory().CreateItem(config, p.GetService<IHealthReporter>()));
            services.AddSingleton<IPipelineFactory, PipelineFactory>();
            services.AddSingleton<DiagnosticPipeline>(p => p.GetService<IPipelineFactory>().CreatePipeline());
            services.AddSingleton<ITelemetryProcessorFactory, EventFlowTelemetryProcessorFactory>();
        }

        public interface IPipelineFactory
        {
            DiagnosticPipeline CreatePipeline();
        }

        private class PipelineFactory : IPipelineFactory
        {
            private readonly IHealthReporter _reporter;
            private readonly IList<IOutput> _outputs;

            public PipelineFactory(IHealthReporter reporter, IEnumerable<IOutput> outputs)
            {
                _reporter = reporter;
                _outputs = outputs.ToImmutableList();
            }

            public DiagnosticPipeline CreatePipeline()
            {
                var aiInput = new ApplicationInsightsInputFactory().CreateItem(null, _reporter);
                var inputs = new IObservable<EventData>[] { aiInput };
                var sinks = _outputs.Select(x => new EventSink(x, null)).ToArray();
                return new DiagnosticPipeline(_reporter, inputs, null, sinks, null, disposeDependencies: true);
            }
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
