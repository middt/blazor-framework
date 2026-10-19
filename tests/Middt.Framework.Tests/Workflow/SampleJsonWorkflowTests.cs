using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Middt.Framework.Model.Enumerations;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Ui.Model;
using Middt.Framework.Ui.Rendering;
using Middt.Framework.Ui.Server;
using Middt.Framework.Ui.Server.Rendering;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;

namespace Middt.Framework.Tests.Workflow;

/// <summary>
/// Comprehensive integration tests for all 5 JSON-backed demo workflows (<c>demo/server/Middt.Sample.Api/Workflows</c>) and
/// <see cref="WorkflowInstanceDataQuery"/> operating purely on <c>WorkflowDbContext</c> JSON documents without relational domain tables.
/// </summary>
public sealed class SampleJsonWorkflowTests
{
    private static readonly WorkflowUser Owner = WorkflowTestHost.User("owner@test.com");
    private static readonly WorkflowUser Alice = WorkflowTestHost.User("alice@test.com", "manager");
    private static readonly WorkflowUser Bob = WorkflowTestHost.User("bob@test.com", "manager", "idp-admin");
    private static readonly ClaimsPrincipal OwnerPrincipal = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "owner@test.com"), new Claim(ClaimTypes.Name, "owner@test.com")],
        "test"));
    private static readonly ClaimsPrincipal StrangerPrincipal = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "stranger@test.com"), new Claim(ClaimTypes.Name, "stranger@test.com")],
        "test"));
    private static readonly ClaimsPrincipal ManagerPrincipal = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "alice@test.com"), new Claim(ClaimTypes.Name, "alice@test.com"), new Claim(ClaimTypes.Role, "manager")],
        "test"));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Middt.Framework.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string[] SampleWorkflowFiles()
    {
        var path = Path.Combine(RepositoryRoot(), "demo", "server", "Middt.Sample.Api", "Workflows");
        return Directory.GetFiles(path, "*.json").Select(File.ReadAllText).ToArray();
    }

    private static Task<WorkflowTestHost> CreateHostAsync(
        FakeHttpHandler? httpHandler = null,
        FakePublisher? publisher = null,
        FakeTimeProvider? time = null) =>
        WorkflowTestHost.StartAsync(
            definitions: SampleWorkflowFiles(),
            services: s =>
            {
                if (publisher is not null)
                {
                    s.AddSingleton<IWorkflowMessagePublisher>(publisher);
                }

                if (httpHandler is not null)
                {
                    s.AddHttpClient(HttpTask.DefaultClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => httpHandler);
                }
            },
            time: time);

    [Fact]
    public async Task Customer_onboarding_supports_auto_approval_manual_kyc_self_note_income_update_rejection_and_unique_setKey()
    {
        await using var host = await CreateHostAsync();

        // 1. Starting customer-onboarding twice with identical default identityNumber ("12345678901") disambiguates businessKey via ensureUnique
        var firstDefault = await host.StartInstanceAsync(
            "customer-onboarding",
            new
            {
                fullName = "Ahmet Yılmaz",
                identityNumber = "12345678901",
                email = "musteri@ornek.com",
                phone = "05321234567",
                segment = "Bireysel",
                monthlyIncome = 65000,
            },
            user: Owner);

        firstDefault.State.Should().Be("active");
        firstDefault.Key.Should().Be("CUST-12345678901");

        var secondDefault = await host.StartInstanceAsync(
            "customer-onboarding",
            new
            {
                fullName = "Ahmet Yılmaz",
                identityNumber = "12345678901",
                email = "musteri@ornek.com",
                phone = "05321234567",
                segment = "Bireysel",
                monthlyIncome = 65000,
            },
            user: Owner);

        secondDefault.State.Should().Be("active");
        secondDefault.Key.Should().StartWith("CUST-12345678901-");

        // 2. Close account transition on firstDefault -> 'closed' (Cancelled)
        var closed = await host.TransitionAsync(firstDefault.Id, "close-account", new { closeReason = "Müşteri talebi ile kapatıldı." }, user: Alice);
        closed.State.Should().Be("closed");
        closed.Status.Should().Be(WorkflowInstanceStatus.Cancelled);

        // 3. Corporate customer goes to manual 'kyc-review', supports $self note, approves to 'active', updates income back to 'kyc-review', and rejects
        var corporate = await host.StartInstanceAsync(
            "customer-onboarding",
            new
            {
                fullName = "Anadolu Lojistik A.Ş.",
                identityNumber = "98765432109",
                email = "finans@anadolulojistik.com",
                phone = "02124440123",
                segment = "Kurumsal",
                monthlyIncome = 450000,
            },
            user: Owner);

        corporate.State.Should().Be("kyc-review");
        corporate.Key.Should().Be("CUST-98765432109");

        // $self transition: add-kyc-note
        corporate = await host.TransitionAsync(corporate.Id, "add-kyc-note", new { lastReviewNote = "Ticaret sicil gazetesi doğrulandı." }, user: Owner);
        corporate.State.Should().Be("kyc-review");
        corporate.Data["lastReviewNote"]!.GetValue<string>().Should().Be("Ticaret sicil gazetesi doğrulandı.");

        // Manager approves KYC -> 'active'
        corporate = await host.TransitionAsync(corporate.Id, "approve-kyc", new { approvalNote = "MASAK uygun" }, user: Alice);
        corporate.State.Should().Be("active");

        // Update income & segment from 'active' -> re-runs 'kyc-scoring' without failing setKey and lands in 'kyc-review'
        corporate = await host.TransitionAsync(corporate.Id, "update-income", new { monthlyIncome = 15000, segment = "KOBİ" }, user: Owner);
        corporate.State.Should().Be("kyc-review");
        corporate.Key.Should().Be("CUST-98765432109");

        // Manager rejects KYC -> 'rejected'
        corporate = await host.TransitionAsync(corporate.Id, "reject-kyc", new { reason = "Risk politikası gereği reddedildi" }, user: Alice);
        corporate.State.Should().Be("rejected");
        corporate.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Credit_allocation_works_both_when_customer_is_missing_and_when_customer_exists_with_subflow_and_4_eyes()
    {
        await using var host = await CreateHostAsync();

        // 1. Start credit-allocation BEFORE any customer-onboarding exists (regression test for null customer reference!)
        var withoutCustomer = await host.StartInstanceAsync(
            "credit-allocation",
            new
            {
                customerKey = "CUST-NONEXISTENT",
                amount = 350000,
                termMonths = 24,
                collateralAmount = 500000,
                iban = "TR330006100519786457841326",
                requireUnderwriting = false,
            },
            user: Bob);

        withoutCustomer.Status.Should().Be(WorkflowInstanceStatus.Active);
        withoutCustomer.State.Should().Be("stage1-approval");
        withoutCustomer.Data["customerName"]!.GetValue<string>().Should().Be("CUST-NONEXISTENT");
        withoutCustomer.Data["ltvRatio"]!.GetValue<decimal>().Should().Be(70m);

        // Complete via approve-stage1 + executive-override (single-user demo flow with idp-admin role)
        withoutCustomer = await host.TransitionAsync(withoutCustomer.Id, "approve-stage1", new { stage1Comment = "Onay" }, user: Bob);
        withoutCustomer.State.Should().Be("stage2-approval");
        withoutCustomer = await host.TransitionAsync(withoutCustomer.Id, "executive-override", new { overrideReason = "Tek kullanıcı demo onayı" }, user: Bob);
        withoutCustomer.State.Should().Be("disbursed");
        withoutCustomer.Status.Should().Be(WorkflowInstanceStatus.Completed);

        // 2. Now create a customer-onboarding instance and verify WorkflowInstanceDataQuery projections + masking
        var customer = await host.StartInstanceAsync(
            "customer-onboarding",
            new
            {
                fullName = "Zeynep Kaya",
                identityNumber = "12345678901",
                email = "zeynep@kaya.com",
                phone = "05329876543",
                segment = "Bireysel",
                monthlyIncome = 65000,
            },
            user: Owner);

        customer.State.Should().Be("active");
        customer.Key.Should().Be("CUST-12345678901");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dataQuery = scope.ServiceProvider.GetRequiredService<WorkflowInstanceDataQuery>();

            var ownerView = await dataQuery.GetAllAsync("CustomerOnboarding", OwnerPrincipal, TestContext.Current.CancellationToken);
            ownerView.Result.Should().Be(ResultEnum.Success);
            ownerView.Data.Should().ContainSingle();
            var row = ownerView.Data![0];
            row["key"]!.GetValue<string>().Should().Be("CUST-12345678901");
            row["fullName"]!.GetValue<string>().Should().Be("Zeynep Kaya");
            row["identityNumber"]!.GetValue<string>().Should().Be("*******8901");
            row.ContainsKey("internalRiskNote").Should().BeFalse();

            var strangerView = await dataQuery.GetAllAsync("CustomerOnboarding", StrangerPrincipal, TestContext.Current.CancellationToken);
            strangerView.Data.Should().BeEmpty();

            var managerView = await dataQuery.GetAllAsync("CustomerOnboarding", ManagerPrincipal, TestContext.Current.CancellationToken);
            managerView.Data.Should().ContainSingle();
            managerView.Data![0]["identityNumber"]!.GetValue<string>().Should().Be("12345678901");
            managerView.Data[0].ContainsKey("internalRiskNote").Should().BeTrue();
        }

        // 3. Start credit-allocation referencing CUST-12345678901 with requireUnderwriting=true and amount=750000
        var credit = await host.StartInstanceAsync(
            "credit-allocation",
            new
            {
                customerKey = "CUST-12345678901",
                amount = 750000,
                termMonths = 36,
                collateralAmount = 1000000,
                iban = "TR330006100519786457841326",
                requireUnderwriting = true,
            },
            user: Alice);

        credit.State.Should().Be("risk-subflow");
        credit.ActiveSubFlow.Should().NotBeNull();
        credit.ActiveSubFlow!.Workflow.Should().Be("risk-limit-eval");
        credit.ActiveSubFlow.State.Should().Be("manual-underwriting");

        // Forward 'approve-underwriting' directly through the parent credit-allocation instance
        credit = await host.TransitionAsync(credit.Id, "approve-underwriting", new { underwriterNote = "Limit uygun" }, user: Alice);
        credit.State.Should().Be("stage1-approval");
        credit.Data["customerName"]!.GetValue<string>().Should().Be("Zeynep Kaya");
        credit.Data["ltvRatio"]!.GetValue<decimal>().Should().Be(75m);

        // Trigger $self transition 'update-collateral' (reenter: true) -> recalculates ltvRatio in onEntry
        credit = await host.TransitionAsync(credit.Id, "update-collateral", new { collateralAmount = 1500000 }, user: Alice);
        credit.State.Should().Be("stage1-approval");
        credit.Data["ltvRatio"]!.GetValue<decimal>().Should().Be(50m);

        // Alice approves stage 1 -> moves to stage2-approval, then returns to stage 1, then approves stage 1 again
        credit = await host.TransitionAsync(credit.Id, "approve-stage1", new { stage1Comment = "Şube onayı verildi" }, user: Alice);
        credit.State.Should().Be("stage2-approval");
        credit = await host.TransitionAsync(credit.Id, "return-to-stage1", user: Alice);
        credit.State.Should().Be("stage1-approval");
        credit = await host.TransitionAsync(credit.Id, "approve-stage1", new { stage1Comment = "Tekrar onaylandı" }, user: Alice);
        credit.State.Should().Be("stage2-approval");

        // 4-Eyes check: Alice ($previousUser) cannot see or trigger 'approve-stage2-four-eyes', but Bob can
        credit.AvailableTransitions.Select(t => t.Key).Should().NotContain("approve-stage2-four-eyes");
        credit = await host.TransitionAsync(credit.Id, "approve-stage2-four-eyes", new { stage2Comment = "Komite 4-göz onayı" }, user: Bob);
        credit.State.Should().Be("disbursed");

        // 4. Over-limit credit (> 5,000,000) is automatically rejected by the risk-limit-eval subflow
        var overLimit = await host.StartInstanceAsync(
            "credit-allocation",
            new
            {
                customerKey = "CUST-12345678901",
                amount = 6000000,
                termMonths = 24,
                collateralAmount = 8000000,
                iban = "TR330006100519786457841326",
                requireUnderwriting = false,
            },
            user: Alice);

        overLimit.State.Should().Be("rejected");
        overLimit.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task Order_payment_orchestration_runs_fanOut_http_gateway_cache_publish_and_error_fallback()
    {
        var httpHandler = new FakeHttpHandler
        {
            StatusCode = HttpStatusCode.OK,
            ResponseBody = """{"status":"Approved","verified":true,"referenceCode":"GW-TEST9999"}""",
        };
        var publisher = new FakePublisher();
        var time = new FakeTimeProvider(WorkflowTestHost.StartTime)
        {
            AutoAdvanceAmount = TimeSpan.FromSeconds(2),
        };

        await using var host = await CreateHostAsync(httpHandler, publisher, time);

        // 1. Standard order (< 50,000) executes fanOut (3 child workflows), HTTP charge, cache, publish, and completes automatically
        var normalOrder = await host.StartInstanceAsync(
            "order-payment-orchestration",
            new
            {
                orderCode = "ORD-2026-1001",
                customerName = "Ahmet Yılmaz",
                amount = 18500,
                paymentMethod = "Kredi Kartı",
            },
            user: Alice);

        normalOrder.State.Should().Be("completed");
        normalOrder.Status.Should().Be(WorkflowInstanceStatus.Completed);
        normalOrder.Key.Should().Be("ORD-ORD-2026-1001");
        normalOrder.Data["paymentReceipt"]!.GetValue<string>().Should().Be("GW-TEST9999");
        normalOrder.Data["parallelChecks"]!.AsArray().Should().HaveCount(3);
        publisher.Published.Should().ContainSingle(p => p.Type == "OrderPaymentCaptured");

        // 2. High-value order (>= 50,000) pauses in 'manual-verification' and completes after manager confirmation
        var highValueOrder = await host.StartInstanceAsync(
            "order-payment-orchestration",
            new
            {
                orderCode = "ORD-2026-1002",
                customerName = "Anadolu Lojistik A.Ş.",
                amount = 65000,
                paymentMethod = "Kurumsal Açık Hesap",
            },
            user: Alice);

        highValueOrder.State.Should().Be("manual-verification");
        highValueOrder = await host.TransitionAsync(highValueOrder.Id, "confirm-order", new { financeNote = "Kurumsal limit onaylandı" }, user: Alice);
        highValueOrder.State.Should().Be("completed");
        highValueOrder.Status.Should().Be(WorkflowInstanceStatus.Completed);

        // 3. Gateway 503 failure triggers smart retry and redirects via 'gateway-fallback' to 'manual-verification', then cancels
        httpHandler.StatusCode = HttpStatusCode.ServiceUnavailable;
        var fallbackOrder = await host.StartInstanceAsync(
            "order-payment-orchestration",
            new
            {
                orderCode = "ORD-2026-1003",
                customerName = "Marmara Ticaret",
                amount = 12000,
                paymentMethod = "Kredi Kartı",
            },
            user: Alice);

        fallbackOrder.State.Should().Be("manual-verification");
        var incidents = await host.IncidentsAsync(fallbackOrder.Id);
        incidents.Should().NotBeEmpty();

        fallbackOrder = await host.TransitionAsync(fallbackOrder.Id, "cancel-order", new { reason = "Ödeme geçidi yanıt vermedi" }, user: Alice);
        fallbackOrder.State.Should().Be("cancelled");
        fallbackOrder.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
    }

    [Fact]
    public async Task Sla_escalation_chains_triage_supports_critical_escalation_self_transitions_dynamic_timer_and_timeout()
    {
        await using var host = await CreateHostAsync();

        // 1. Yüksek priority ticket: chains triage -> priority-routing -> waiting-agent
        var ticket = await host.StartInstanceAsync(
            "sla-escalation",
            new
            {
                ticketTitle = "API Gecikmesi",
                customerName = "Marmara Finans",
                amount = 30000,
                severity = "Yüksek",
                slaDuration = "00:02:00",
            },
            user: Alice);

        ticket.State.Should().Be("waiting-agent");
        ticket.Data["assignedQueue"]!.GetValue<string>().Should().Be("Kıdemli Operasyon");

        // $self transition without reenter increments heartbeatCount
        ticket = await host.TransitionAsync(ticket.Id, "heartbeat-ping", new { progressNote = "Loglar inceleniyor" }, user: Alice);
        ticket.State.Should().Be("waiting-agent");
        ticket.Data["heartbeatCount"]!.GetValue<decimal>().Should().Be(1m);

        // $self transition with reenterreschedules dynamic SLA timer to 5 minutes
        ticket = await host.TransitionAsync(ticket.Id, "extend-sla", new { slaDuration = "00:05:00" }, user: Alice);
        ticket.State.Should().Be("waiting-agent");

        // After 2 minutes it should still be waiting-agent because SLA was extended to 5 minutes
        await host.RunJobsAsync(TimeSpan.FromMinutes(2));
        (await host.GetAsync(ticket.Id, user: Alice)).State.Should().Be("waiting-agent");

        // Advance another 3 minutes + 1s -> dynamic timer fires and moves to 'escalated'
        await host.RunJobsAsync(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
        var escalated = await host.GetAsync(ticket.Id, user: Alice);
        escalated.State.Should().Be("escalated");

        // Verify order-escalated layout renders cleanly for sla-escalation without any '⚠' expression error
        var layoutPath = Path.Combine(RepositoryRoot(), "demo", "server", "Middt.Sample.Api", "Layouts", "order-escalated.json");
        var layout = UiJson.Parse(File.ReadAllText(layoutPath));
        var uiOptions = new MiddtUiOptions();
        var renderedHtml = new UiHtmlRenderer(uiOptions.Widgets, Options.Create(uiOptions)).Render(
            layout,
            new Dictionary<string, JsonNode?>
            {
                [UiExpressions.Data] = escalated.Data,
                [UiExpressions.Instance] = new JsonObject { ["stateTitle"] = escalated.StateTitle, ["workflow"] = escalated.Workflow },
            },
            new Middt.Framework.Expressions.ExpressionUser { Name = "alice", Roles = ["manager"] },
            CultureInfo.InvariantCulture);
        renderedHtml.Should().NotContain("⚠");

        // Manager resolves the escalated ticket
        var resolved = await host.TransitionAsync(ticket.Id, "manager-resolve", new { resolutionNote = "Kök neden giderildi" }, user: Alice);
        resolved.State.Should().Be("resolved");
        resolved.Status.Should().Be(WorkflowInstanceStatus.Completed);

        // 2. Kritik priority ticket escalates immediately on start
        var critical = await host.StartInstanceAsync(
            "sla-escalation",
            new
            {
                ticketTitle = "Ana Veritabanı Kesintisi",
                customerName = "Genel Müdürlük",
                amount = 250000,
                severity = "Kritik",
                slaDuration = "00:05:00",
            },
            user: Alice);

        critical.State.Should().Be("escalated");
        critical.Data["assignedQueue"]!.GetValue<string>().Should().Be("Kriz Masası");

        // 3. Workflow-level 24h timeout transitions an uncompleted ticket to 'expired'
        await host.RunJobsAsync(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var expired = await host.GetAsync(critical.Id, user: Alice);
        expired.State.Should().Be("expired");
        expired.StateSubType.Should().Be("Timeout");
    }

    [Fact]
    public async Task Risk_limit_eval_supports_auto_approval_manual_underwriting_and_over_limit_rejection()
    {
        await using var host = await CreateHostAsync();

        // 1. Standard amount (<= 500,000) auto-approves
        var approved = await host.StartInstanceAsync(
            "risk-limit-eval",
            new
            {
                customerKey = "CUST-12345678901",
                amount = 200000,
                checkType = "Kredi Limit Analizi",
                requireUnderwriting = false,
            },
            user: Alice);

        approved.State.Should().Be("approved");
        approved.StateSubType.Should().Be("Success");
        approved.Data["riskScore"]!.GetValue<int>().Should().Be(810);
        approved.Data["recommendedLimit"]!.GetValue<decimal>().Should().Be(250000m);

        // 2. High amount (> 500,000) with requireUnderwriting=true pauses in manual-underwriting and supports rejection
        var underwriting = await host.StartInstanceAsync(
            "risk-limit-eval",
            new
            {
                customerKey = "CUST-12345678901",
                amount = 800000,
                checkType = "Ticari Limit Analizi",
                requireUnderwriting = true,
            },
            user: Alice);

        underwriting.State.Should().Be("manual-underwriting");
        underwriting = await host.TransitionAsync(underwriting.Id, "reject-underwriting", new { reason = "Finansal rasyolar yetersiz" }, user: Alice);
        underwriting.State.Should().Be("rejected");
        underwriting.StateSubType.Should().Be("Error");

        // 3. Over-limit amount (> 5,000,000) auto-rejects immediately
        var rejected = await host.StartInstanceAsync(
            "risk-limit-eval",
            new
            {
                customerKey = "CUST-12345678901",
                amount = 5500000,
                checkType = "Kurumsal Limit Analizi",
                requireUnderwriting = false,
            },
            user: Alice);

        rejected.State.Should().Be("rejected");
        rejected.StateSubType.Should().Be("Error");
    }
}
