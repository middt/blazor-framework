using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middt.Framework.Common.Observability;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Tests.Workflow;
using Middt.Framework.Workflow;
using Middt.Framework.Workflow.Diagnostics;
using OpenTelemetry.Exporter;

namespace Middt.Framework.Tests.Observability;

public sealed class OpenObserveObservabilityTests
{
    [Theory]
    [InlineData("http://otel-collector:4318", "http/protobuf", "traces", "http://otel-collector:4318/v1/traces")]
    [InlineData("http://openobserve:5080/api/default", "http/protobuf", "traces", "http://openobserve:5080/api/default/v1/traces")]
    [InlineData("http://openobserve:5080/api/default/", "http/protobuf", "logs", "http://openobserve:5080/api/default/v1/logs")]
    [InlineData("https://jaeger.example.com:4318/v1/traces", "http/protobuf", "metrics", "https://jaeger.example.com:4318/v1/metrics")]
    [InlineData("http://otel-collector:4317", "grpc", "traces", "http://otel-collector:4317/")]
    public void BuildSignalEndpoint_normalizes_otlp_urls_for_http_protobuf_and_grpc(
        string endpoint,
        string protocol,
        string signal,
        string expected)
    {
        var options = new MiddtOtlpOptions
        {
            Endpoint = endpoint,
            Protocol = protocol,
        };

        options.IsEnabled.Should().BeTrue();
        options.BuildSignalEndpoint(signal).ToString().Should().Be(expected);
    }

    [Fact]
    public void BuildHeaders_combines_string_and_dictionary_headers()
    {
        var options = new MiddtOtlpOptions
        {
            Endpoint = "http://openobserve:5080/api/default",
            Headers = "Authorization=Basic YWRtaW5AbWlkZHQubG9jYWw6Q29tcGxleHBhc3MjMTIz",
        };
        options.HeaderValues["stream-name"] = "middt-logs";
        options.HeaderValues["organization"] = "default";

        options.BuildHeaders().Should().Be(
            "Authorization=Basic YWRtaW5AbWlkZHQubG9jYWw6Q29tcGxleHBhc3MjMTIz,stream-name=middt-logs,organization=default");
    }

    [Fact]
    public void ResolveActiveExporters_binds_standard_OTEL_EXPORTER_OTLP_environment_keys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://openobserve:5080/api/default",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_EXPORTER_OTLP_HEADERS"] = "Authorization=Basic dGVzdA==,stream-name=default",
            })
            .Build();

        var telemetry = new MiddtTelemetryOptions();
        var exporters = telemetry.ResolveActiveExporters(config);

        exporters.Should().ContainSingle();
        exporters[0].ResolveProtocol().Should().Be(OtlpExportProtocol.HttpProtobuf);
        exporters[0].BuildSignalEndpoint("traces").ToString().Should().Be("http://openobserve:5080/api/default/v1/traces");
        exporters[0].BuildHeaders().Should().Be("Authorization=Basic dGVzdA==,stream-name=default");
    }

    [Fact]
    public void AddMiddtOpenTelemetry_supports_multiple_exporters_from_config()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Middt.TestHost",
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telemetry:ServiceName"] = "middt-api",
            ["Telemetry:Otlp:Endpoint"] = "http://openobserve:5080/api/default",
            ["Telemetry:Otlp:Protocol"] = "http/protobuf",
            ["Telemetry:Otlp:Headers"] = "Authorization=Basic dGVzdA==,stream-name=default",
            ["Telemetry:Exporters:0:Endpoint"] = "http://jaeger:4318",
            ["Telemetry:Exporters:0:Protocol"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://otel-collector:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
        });

        builder.AddMiddtOpenTelemetry();

        using var host = builder.Build();
        var bound = host.Services.GetRequiredService<MiddtTelemetryOptions>();
        var exporters = bound.ResolveActiveExporters(builder.Configuration);

        exporters.Should().HaveCount(3);
        exporters[0].BuildSignalEndpoint("traces").ToString().Should().Be("http://openobserve:5080/api/default/v1/traces");
        exporters[1].BuildSignalEndpoint("traces").ToString().Should().Be("http://jaeger:4318/v1/traces");
        exporters[2].BuildSignalEndpoint("traces").ToString().Should().Be("http://otel-collector:4317/");
    }

    [Theory]
    [InlineData("/api/workflows/customer-onboarding/instances", "/api/{**catch-all}", "/api/workflows/{workflow}/instances")]
    [InlineData("/api/workflows/customer-onboarding/instances/0199fc2e-8789-7252-9142-cfbd22b98a82", "api/workflows/{workflow}/instances/{id:guid}", "/api/workflows/{workflow}/instances/{id}")]
    [InlineData("/api/workflows/customer-onboarding/instances/0199fc2e-8789-7252-9142-cfbd22b98a82/transitions/submit", "api/workflows/{workflow}/instances/{id:guid}/transitions/{transition}", "/api/workflows/{workflow}/instances/{id}/transitions/{transition}")]
    [InlineData("/api/ui/layouts/order-monitor", "api/ui/layouts/{key}", "/api/ui/layouts/{key}")]
    public void MiddtRouteActivityProcessor_normalizes_catch_all_and_httpclient_spans_to_grouped_routes(
        string urlPath,
        string rawRoute,
        string expectedRoute)
    {
        using var source = new ActivitySource("Middt.RouteTest");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Middt.RouteTest",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("GET /api/{**catch-all}", ActivityKind.Server)!;
        activity.SetTag("http.request.method", "GET");
        activity.SetTag("http.route", rawRoute);
        activity.SetTag("url.path", urlPath);

        var processor = new MiddtRouteActivityProcessor();
        processor.OnEnd(activity);

        activity.DisplayName.Should().Be($"GET {expectedRoute}");
        activity.GetTagItem("http.route").Should().Be(expectedRoute);
    }

    [Theory]
    [InlineData("SELECT i.id, i.workflow_key FROM wf.instances AS i WHERE i.workflow_key = $1", "DB SELECT wf.instances")]
    [InlineData("INSERT INTO \"wf\".\"instance_transitions\" (\"id\") VALUES ($1)", "DB INSERT wf.instance_transitions")]
    [InlineData("UPDATE wf.instances SET status = $1 WHERE id = $2", "DB UPDATE wf.instances")]
    [InlineData("INSERT INTO \"wf\".\"WorkflowHistory\" (\"Id\") VALUES ($1);\nUPDATE \"wf\".\"WorkflowInstances\" SET \"State\" = $2;\nUPDATE \"wf\".\"WorkflowInstanceData\" SET \"Data\" = $3;", "DB INSERT wf.WorkflowHistory + UPDATE wf.WorkflowInstances + UPDATE wf.WorkflowInstanceData")]
    public void MiddtRouteActivityProcessor_enriches_database_spans_with_verb_and_table(
        string sql,
        string expectedDisplayName)
    {
        using var source = new ActivitySource("Npgsql");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("middt_workflow", ActivityKind.Client)!;
        activity.SetTag("db.system", "postgresql");
        activity.SetTag("db.statement", sql);

        var processor = new MiddtRouteActivityProcessor();
        processor.OnEnd(activity);

        activity.DisplayName.Should().Be(expectedDisplayName);
    }

    [Fact]
    public async Task WorkflowEngine_emits_OpenTelemetry_activities_and_metrics_for_transitions_and_tasks()
    {
        const string definitionJson = """
            {
              "key": "otel-order-approval",
              "version": 1,
              "start": { "key": "start", "target": "done" },
              "states": [
                {
                  "key": "done",
                  "type": "Finish",
                  "subType": "Success",
                  "onEntry": [
                    { "key": "mark-complete", "task": "record" }
                  ]
                }
              ]
            }
            """;

        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("middt.workflow.key") as string == "otel-order-approval")
                {
                    lock (activities)
                    {
                        activities.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        long transitionsCount = 0;
        long tasksCount = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WorkflowDiagnostics.SourceName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var isTargetWorkflow = false;
            foreach (var tag in tags)
            {
                if (tag.Key == "workflow" && tag.Value as string == "otel-order-approval")
                {
                    isTargetWorkflow = true;
                    break;
                }
            }

            if (!isTargetWorkflow)
            {
                return;
            }

            if (instrument.Name == "middt.workflow.transitions.executed")
            {
                Interlocked.Add(ref transitionsCount, measurement);
            }
            else if (instrument.Name == "middt.workflow.tasks.executed")
            {
                Interlocked.Add(ref tasksCount, measurement);
            }
        });
        meterListener.Start();

        await using var host = await WorkflowTestHost.StartAsync(definitionJson);
        var instance = await host.StartInstanceAsync(
            "otel-order-approval",
            new { amount = 500, customer = "OTLP Exporter Test" },
            key: $"OTEL-{Guid.NewGuid():N}");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);

        lock (activities)
        {
            activities.Should().Contain(a =>
                a.OperationName == "workflow.start otel-order-approval");
            activities.Should().Contain(a =>
                a.OperationName.StartsWith("workflow.transition otel-order-approval/", StringComparison.Ordinal) &&
                (string?)a.GetTagItem("middt.workflow.instance_id") == instance.Id.ToString());
            activities.Should().Contain(a =>
                a.OperationName.StartsWith("workflow.task ", StringComparison.Ordinal) &&
                (string?)a.GetTagItem("middt.workflow.instance_id") == instance.Id.ToString());
            activities.Should().Contain(a =>
                a.OperationName == "workflow.persist otel-order-approval" &&
                (string?)a.GetTagItem("middt.workflow.instance_id") == instance.Id.ToString());
        }

        Interlocked.Read(ref transitionsCount).Should().BeGreaterThanOrEqualTo(1);
        Interlocked.Read(ref tasksCount).Should().BeGreaterThanOrEqualTo(1);
    }
}
