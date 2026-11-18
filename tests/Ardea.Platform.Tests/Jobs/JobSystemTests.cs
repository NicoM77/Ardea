using Ardea.Platform.Jobs;
using Ardea.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Platform.Tests.Jobs;

/// <summary>Acceptance criteria of phase 0 for the job system (master plan 3.4, 14).</summary>
public sealed class JobSystemTests(PostgresServer server)
{
    private static readonly JobWorkerOptions Fast = new()
    {
        Concurrency = 8,
        PollInterval = TimeSpan.FromMilliseconds(200),
        Lease = TimeSpan.FromSeconds(1.5),
        WatchdogInterval = TimeSpan.FromMilliseconds(250),
        BackoffBase = TimeSpan.FromMilliseconds(50),
        ShutdownGrace = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public async Task Job_and_its_cause_are_committed_together_or_not_at_all()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<EffectJob, EffectJobHandler>());

        // Forced abort: the unit of work ends without commit after both writes.
        await host.InUnitOfWorkAsync(WriteCauseAndJobAsync, commit: false);
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM public.test_business"));
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'test.effect'"));

        await host.InUnitOfWorkAsync(WriteCauseAndJobAsync);
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM public.test_business"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE kind = 'test.effect'"));

        static async Task WriteCauseAndJobAsync(IServiceProvider services)
        {
            var connection = await services.GetRequiredService<Ardea.Platform.Persistence.IUnitOfWork>().GetConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand("INSERT INTO public.test_business (name) VALUES ('entry changed')", connection);
            await command.ExecuteNonQueryAsync();
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(new EffectJob("follow-up"));
        }
    }

    [Fact]
    public async Task Ten_thousand_jobs_run_exactly_once_on_two_workers_even_when_one_is_killed()
    {
        const int total = 10_000;
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<EffectJob, EffectJobHandler>(new JobKindOptions { MaxConcurrency = 8 }));
        await host.InUnitOfWorkAsync(async services =>
        {
            var queue = services.GetRequiredService<IJobQueue>();
            for (var i = 0; i < total; i++)
            {
                await queue.EnqueueAsync(new EffectJob($"job-{i}"));
            }
        });

        var doomed = host.StartWorker("worker-a", Fast);
        host.StartWorker("worker-b", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'succeeded'", total / 4, TimeSpan.FromSeconds(60));

        // Kill worker A in the middle of its work; a replacement joins.
        doomed.SimulateCrash();
        host.StartWorker("worker-c", Fast);

        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'succeeded'", total, TimeSpan.FromSeconds(90));
        Assert.Equal(total, await host.CountAsync("SELECT count(*) FROM public.test_effect"));
        Assert.Equal(total, await host.CountAsync("SELECT count(DISTINCT job_ref) FROM public.test_effect"));
        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM platform.job WHERE state <> 'succeeded'"));
        // Whether the kill caught a job mid-flight depends on timing here; the gated test below
        // proves deterministically that stranded jobs run again with their effect exactly once.
    }

    [Fact]
    public async Task Jobs_of_a_killed_worker_run_again_and_their_effect_happens_once()
    {
        var gate = new Gate();
        await using var host = await JobTestHost.CreateAsync(server, s =>
        {
            s.AddSingleton(gate);
            s.AddJob<GatedJob, GatedJobHandler>();
        });
        await host.InUnitOfWorkAsync(async services =>
        {
            var queue = services.GetRequiredService<IJobQueue>();
            for (var i = 0; i < 3; i++)
            {
                await queue.EnqueueAsync(new GatedJob($"gated-{i}"));
            }
        });

        var doomed = host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'running'", 3, TimeSpan.FromSeconds(10));
        doomed.SimulateCrash();

        gate.Open.SetResult();
        host.StartWorker("worker-b", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'succeeded'", 3, TimeSpan.FromSeconds(15));

        Assert.Equal(3, await host.CountAsync("SELECT count(*) FROM public.test_effect"));
        Assert.Equal(3, await host.CountAsync("SELECT count(*) FROM platform.job WHERE attempts = 2"));
    }

    [Fact]
    public async Task Waiting_jobs_coalesce_but_a_running_one_does_not_block_a_new_one()
    {
        var gate = new Gate();
        await using var host = await JobTestHost.CreateAsync(server, s =>
        {
            s.AddSingleton(gate);
            s.AddJob<GatedJob, GatedJobHandler>();
        });
        var keyed = new EnqueueOptions { CoalesceKey = "match-entry:CVE-2026-1" };

        bool first = false, second = false, third = false;
        await host.InUnitOfWorkAsync(async s => first = await s.GetRequiredService<IJobQueue>().EnqueueAsync(new GatedJob("a"), keyed));
        await host.InUnitOfWorkAsync(async s => second = await s.GetRequiredService<IJobQueue>().EnqueueAsync(new GatedJob("b"), keyed));
        Assert.True(first);
        Assert.False(second);

        host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'running'", 1, TimeSpan.FromSeconds(10));
        await host.InUnitOfWorkAsync(async s => third = await s.GetRequiredService<IJobQueue>().EnqueueAsync(new GatedJob("c"), keyed));
        Assert.True(third);

        gate.Open.SetResult();
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'succeeded'", 2, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Failing_job_is_retried_with_backoff_and_given_up_after_its_attempts()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<FailingJob, FailingJobHandler>(new JobKindOptions { MaxAttempts = 3 }));
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new FailingJob("x")));

        host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'dead'", 1, TimeSpan.FromSeconds(15));

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE attempts = 3 AND last_error LIKE '%always fails%'"));
    }

    [Fact]
    public async Task Single_attempt_job_fails_for_good_without_retry()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<SingleShotJob, SingleShotJobHandler>(new JobKindOptions { MaxAttempts = 1 }));
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new SingleShotJob("sync")));

        host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'failed'", 1, TimeSpan.FromSeconds(10));

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE attempts = 1"));
    }

    [Fact]
    public async Task Job_over_its_time_limit_is_cancelled_and_counts_as_failed()
    {
        await using var host = await JobTestHost.CreateAsync(server, s =>
            s.AddJob<HangingJob, HangingJobHandler>(new JobKindOptions { MaxAttempts = 1, Timeout = TimeSpan.FromSeconds(1) }));
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new HangingJob("hang")));

        host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'failed'", 1, TimeSpan.FromSeconds(10));

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE last_error LIKE 'Timed out%'"));
    }

    [Fact]
    public async Task Worker_that_lost_its_lease_rolls_its_work_back()
    {
        var gate = new Gate();
        await using var host = await JobTestHost.CreateAsync(server, s =>
        {
            s.AddSingleton(gate);
            s.AddJob<GatedJob, GatedJobHandler>();
        });
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new GatedJob("contested")));

        var worker = host.StartWorker("worker-a", Fast with { Lease = TimeSpan.FromMinutes(5) });
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'running'", 1, TimeSpan.FromSeconds(10));

        // Another claim takes the job over, as after a lease that ran out during a network partition;
        // it may even be the same worker id (two processes on one host share it).
        await host.ExecuteAsync("UPDATE platform.job SET lease_token = gen_random_uuid(), lease_until = now() + interval '5 minutes'");
        gate.Open.SetResult();
        await WaitUntilIdleAsync(worker);

        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM public.test_effect"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE state = 'running' AND lease_owner = 'worker-a'"));
    }

    [Fact]
    public async Task Stopping_worker_lets_running_jobs_finish_within_the_grace_period()
    {
        var gate = new Gate();
        await using var host = await JobTestHost.CreateAsync(server, s =>
        {
            s.AddSingleton(gate);
            s.AddJob<GatedJob, GatedJobHandler>();
        });
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new GatedJob("deploy")));
        var worker = host.StartWorker("worker-a", Fast with { ShutdownGrace = TimeSpan.FromSeconds(10), Lease = TimeSpan.FromSeconds(30) });
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'running'", 1, TimeSpan.FromSeconds(10));

        var stopping = worker.StopAsync(CancellationToken.None);
        await Task.Delay(300);
        gate.Open.SetResult();
        await stopping;

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE state = 'succeeded' AND attempts = 1"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM public.test_effect"));
    }

    [Fact]
    public async Task Job_still_running_after_the_grace_period_is_given_back_without_counting_the_attempt()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<HangingJob, HangingJobHandler>());
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new HangingJob("long")));
        var worker = host.StartWorker("worker-a", Fast with { ShutdownGrace = TimeSpan.FromMilliseconds(300), Lease = TimeSpan.FromSeconds(30) });
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'running'", 1, TimeSpan.FromSeconds(10));

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE state = 'pending' AND attempts = 0 AND lease_token IS NULL"));
    }

    [Fact]
    public async Task Expired_job_with_a_waiting_successor_is_superseded_and_finished()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<EffectJob, EffectJobHandler>());
        await host.ExecuteAsync(
            """
            INSERT INTO platform.job (kind, payload, coalesce_key, run_at, priority, state, attempts, max_attempts, timeout_seconds, lease_owner, lease_until, lease_token)
            VALUES ('test.effect', '{"ref":"old"}', 'k', now(), 1, 'running', 1, 5, 60, 'gone', now() - interval '1 minute', gen_random_uuid()),
                   ('test.effect', '{"ref":"new"}', 'k', now(), 1, 'pending', 0, 5, 60, NULL, NULL, NULL)
            """);

        await JobStore.ReclaimExpiredAsync(host.DataSource, CancellationToken.None);

        Assert.Equal(1, await host.CountAsync(
            "SELECT count(*) FROM platform.job WHERE payload->>'ref' = 'old' AND state = 'succeeded' AND finished_at IS NOT NULL AND last_error LIKE '%Superseded%'"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE payload->>'ref' = 'new' AND state = 'pending'"));
    }

    [Fact]
    public async Task Handler_cannot_commit_the_jobs_unit_of_work_itself()
    {
        await using var host = await JobTestHost.CreateAsync(server, s => s.AddJob<SelfCommittingJob, SelfCommittingJobHandler>(new JobKindOptions { MaxAttempts = 1 }));
        await host.InUnitOfWorkAsync(s => s.GetRequiredService<IJobQueue>().EnqueueAsync(new SelfCommittingJob("x")));

        host.StartWorker("worker-a", Fast);
        await host.WaitForAsync("SELECT count(*) FROM platform.job WHERE state = 'failed'", 1, TimeSpan.FromSeconds(10));

        Assert.Equal(0, await host.CountAsync("SELECT count(*) FROM public.test_effect"));
        Assert.Equal(1, await host.CountAsync("SELECT count(*) FROM platform.job WHERE last_error LIKE '%committed by the job system%'"));
    }

    private static async Task WaitUntilIdleAsync(JobProcessor worker)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (worker.RunningCount > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, worker.RunningCount);
    }
}
